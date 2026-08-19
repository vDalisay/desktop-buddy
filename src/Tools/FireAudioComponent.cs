using System;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.Tools;

public enum FireAudioCue
{
    None = 0,

    /// <summary>The looped-by-chunks hiss of the stream while primary is held.</summary>
    Hiss = 1,

    /// <summary>The soft whumpf of a fresh ignition.</summary>
    Ignition = 2,
}

/// <summary>
/// Provisional clean-room audio seam for the Fire Sprayer, on exactly the
/// <see cref="SwingAudioComponent"/>/<see cref="GrenadeAudioComponent"/> idiom: short PCM
/// clips synthesized once at startup at 22 050 Hz and routed through one authored player —
/// no sampled source file and no audio-server volume mutation.
///
/// <para>The hiss is a loop built out of chunks rather than a streamed sound: the component
/// replays one seamless clip while the stream is emitting and stops on the tick it is
/// released, so what the player hears ends exactly when the spray does. Deciding whether the
/// sprayer is emitting is <see cref="FireSprayerComponent"/>'s business, not this
/// component's; this plays what it is told and counts what it played, so the counters are
/// scenario oracles.</para>
/// </summary>
[GlobalClass]
public partial class FireAudioComponent : Node
{
    private const int MixRate = 22_050;

    /// <summary>
    /// How far a burn take is faded under its authored level while it enters and leaves. The
    /// takes are mp3, whose encoder padding makes a hard seam audible, so consecutive takes
    /// overlap by <see cref="CrossfadeSeconds"/> instead of butting up against each other.
    /// </summary>
    private const float LoopFadeDb = 9.0f;
    private const double LoopFadeSeconds = 0.12;
    private const double CrossfadeSeconds = 0.22;

    [Export] public FireSprayerComponent Sprayer { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;
    [Export] public AudioStreamPlayer LoopPlayer { get; set; } = null!;

    // Optional owner-authored replacements. Null keeps the clean-room fallback below.
    [Export] public AudioStream? IgnitionStream { get; set; }
    [Export] public AudioStream? IgnitionStream2 { get; set; }
    [Export] public AudioStream? BurnLoopStream { get; set; }
    [Export] public AudioStream? BurnLoopStream2 { get; set; }

    private AudioStream _hiss = null!;
    private AudioStream[] _burnTakes = Array.Empty<AudioStream>();
    private AudioStreamPlayer _burnB = null!;
    private readonly Tween?[] _fades = new Tween?[2];
    private bool _burnBIsActive;
    private bool _burnRunning;
    private int _nextTakeIndex;
    private AudioStream _ignition = null!;

    public bool IsInitialized { get; private set; }
    public int GeneratedStreamCount { get; private set; }
    public int PlayCount { get; private set; }
    public int IgnitionCueCount { get; private set; }
    public int HissStartCount { get; private set; }
    public int HissStopCount { get; private set; }
    public FireAudioCue LastCue { get; private set; }
    public bool IsHissing { get; private set; }
    public StringName RoutedBus => Player.Bus;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Sprayer) || !Sprayer.IsInitialized ||
            !GodotObject.IsInstanceValid(Player) || !GodotObject.IsInstanceValid(LoopPlayer))
        {
            throw new InvalidOperationException(
                "FireAudioComponent requires an initialized sprayer and both players.");
        }

        Player.Bus = AudioMix.Sfx;
        LoopPlayer.Bus = AudioMix.Sfx;

        // Band-limited noise with a slow breath under it: a gas flame, not a hi-hat. The
        // clip is authored to loop cleanly by cross-fading its own tail into its head.
        AudioStreamWav fallbackHiss = Synthesize(
            seconds: 0.50,
            loop: true,
            (sample, progress) =>
            {
                double noise = DeterministicNoise(sample * 3 + 17);
                double lowPassed =
                    (noise + DeterministicNoise((sample * 3) + 16) +
                     DeterministicNoise((sample * 3) + 15)) / 3.0;
                double breath = 0.82 + (0.18 * Math.Sin(Math.Tau * 3.5 * progress));
                return lowPassed * breath * 0.28;
            });
        // A short soft thump with a bright edge: fire catching, never an explosion.
        AudioStreamWav fallbackIgnition = Synthesize(
            seconds: 0.22,
            loop: false,
            (sample, progress) =>
            {
                double sweep = Lerp(180.0, 60.0, Math.Sqrt(progress));
                double body = Math.Sin(Math.Tau * sweep * sample / MixRate);
                double air = DeterministicNoise(sample * 7 + 43) *
                             Math.Pow(1.0 - progress, 2.0);
                double attack = progress < 0.03 ? progress / 0.03 : 1.0;
                return ((body * 0.55) + (air * 0.45)) * attack *
                       Math.Pow(1.0 - progress, 1.8) * 0.36;
            });
        // The burn loop is picked per spray by hand rather than through a randomizer: the
        // stream has to keep looping while the trigger is held, and a randomizer would both
        // re-roll the take and re-roll its pitch on every repeat, warbling the flame.
        var burnTakes = new System.Collections.Generic.List<AudioStream>(2);
        foreach (AudioStream? take in new[] { BurnLoopStream, BurnLoopStream2 })
        {
            if (SfxRandomizer.IsValid(take))
                burnTakes.Add(take!);
        }

        _burnTakes = burnTakes.ToArray();
        _hiss = _burnTakes.Length > 0 ? _burnTakes[0] : fallbackHiss;
        _ignition = SfxRandomizer.Pick(2.0f, IgnitionStream, IgnitionStream2) ?? fallbackIgnition;
        GeneratedStreamCount = 2;

        _burnB = new AudioStreamPlayer
        {
            Name = "FireBurnPlayerB",
            Bus = LoopPlayer.Bus,
            ProcessMode = LoopPlayer.ProcessMode,
            MaxPolyphony = 1,
        };
        AddChild(_burnB);
        SetProcess(true);

        Sprayer.SprayingChanged += OnSprayingChanged;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Sprayer))
        {
            Sprayer.SprayingChanged -= OnSprayingChanged;
        }

        foreach (AudioStreamPlayer player in new[] { Player, LoopPlayer, _burnB })
        {
            if (GodotObject.IsInstanceValid(player))
            {
                player.Stop();
                player.Stream = null;
            }
        }
    }

    private void OnSprayingChanged(bool spraying)
    {
        if (spraying)
        {
            // Pulling the trigger is the ignition. The burn takes are what the stream sounds
            // like while it is running, so they follow the ignition rather than waiting for
            // something to catch fire (owner instruction 2026-08-19).
            PlayIgnition();
            _burnRunning = false;
            _nextTakeIndex = _burnTakes.Length > 1 ? GD.RandRange(0, _burnTakes.Length - 1) : 0;
            IsHissing = true;
            HissStartCount++;
            return;
        }

        StopBurn();
        if (IsHissing)
            HissStopCount++;
        IsHissing = false;
    }

    /// <summary>
    /// Runs the ignition -> burn -> burn chain. Each take is played once and the next one is
    /// started far enough before it ends that the two overlap, so holding the trigger cycles
    /// between the burn takes without a seam, and the ignition hands over the same way.
    /// </summary>
    public override void _Process(double delta)
    {
        if (!IsInitialized || !IsHissing || _burnTakes.Length == 0)
            return;

        if (!_burnRunning)
        {
            if (RemainingSeconds(Player) > CrossfadeSeconds)
                return;

            StartNextBurnTake();
            _burnRunning = true;
            return;
        }

        if (RemainingSeconds(ActiveBurnPlayer) <= CrossfadeSeconds)
            StartNextBurnTake();
    }

    private AudioStreamPlayer ActiveBurnPlayer => _burnBIsActive ? _burnB : LoopPlayer;

    private void StartNextBurnTake()
    {
        AudioStreamPlayer outgoing = ActiveBurnPlayer;
        AudioStreamPlayer incoming = _burnBIsActive ? LoopPlayer : _burnB;
        _burnBIsActive = !_burnBIsActive;

        _hiss = _burnTakes[_nextTakeIndex % _burnTakes.Length];
        // Straight alternation rather than a random pick: with two takes a random choice
        // repeats one of them half the time, which is exactly the sameness the takes exist
        // to break up.
        _nextTakeIndex = (_nextTakeIndex + 1) % _burnTakes.Length;

        float authored = Sprayer.Profile.AudioVolumeDb;
        incoming.Stop();
        incoming.Stream = _hiss;
        incoming.PitchScale = 1.0f + ((float)GD.RandRange(-4.0, 4.0) * 0.01f);
        incoming.VolumeDb = authored - LoopFadeDb;
        incoming.Play();
        Fade(incoming, authored, CrossfadeSeconds);
        if (outgoing.Playing)
            Fade(outgoing, authored - LoopFadeDb, CrossfadeSeconds, thenStop: true);

        PlayCount++;
        LastCue = FireAudioCue.Hiss;
    }

    private void StopBurn()
    {
        _burnRunning = false;
        float floor = Sprayer.Profile.AudioVolumeDb - LoopFadeDb;
        foreach (AudioStreamPlayer player in new[] { LoopPlayer, _burnB })
        {
            if (GodotObject.IsInstanceValid(player) && player.Playing)
                Fade(player, floor, LoopFadeSeconds, thenStop: true);
        }
    }

    private static double RemainingSeconds(AudioStreamPlayer player)
    {
        if (!GodotObject.IsInstanceValid(player) || !player.Playing || player.Stream is null)
            return 0.0;

        double pitch = Math.Max(0.01f, player.PitchScale);
        return Math.Max(0.0, player.Stream.GetLength() - player.GetPlaybackPosition()) / pitch;
    }

    private void PlayIgnition()
    {
        Player.VolumeDb = Sprayer.Profile.AudioVolumeDb;
        Player.Stream = _ignition;
        Player.Play();
        IgnitionCueCount++;
        PlayCount++;
        LastCue = FireAudioCue.Ignition;
    }

    private void Fade(AudioStreamPlayer player, float targetDb, double seconds, bool thenStop = false)
    {
        int slot = ReferenceEquals(player, _burnB) ? 1 : 0;
        if (_fades[slot] is { } running && running.IsValid())
            running.Kill();

        Tween fade = CreateTween();
        _fades[slot] = fade;
        fade.TweenProperty(player, "volume_db", targetDb, seconds);
        if (thenStop)
            fade.TweenCallback(Callable.From(player.Stop));
    }

    private static AudioStreamWav Synthesize(
        double seconds,
        bool loop,
        Func<int, double, double> sampleAt)
    {
        int samples = Math.Max(1, (int)Math.Round(seconds * MixRate));
        var data = new byte[samples * 2];
        for (int sample = 0; sample < samples; sample++)
        {
            double progress = sample / (double)samples;
            double normalized = Math.Clamp(sampleAt(sample, progress), -1.0, 1.0);
            short pcm = (short)Math.Round(normalized * short.MaxValue);
            data[sample * 2] = (byte)(pcm & 0xff);
            data[sample * 2 + 1] = (byte)((pcm >> 8) & 0xff);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = data,
            LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled,
            LoopBegin = 0,
            LoopEnd = loop ? samples - 1 : 0,
        };
    }

    private static double DeterministicNoise(int sample)
    {
        uint value = unchecked((uint)sample * 747_796_405u + 2_891_336_453u);
        value = ((value >> ((int)(value >> 28) + 4)) ^ value) * 277_803_737u;
        value = (value >> 22) ^ value;
        return (value / (double)uint.MaxValue) * 2.0 - 1.0;
    }

    private static double Lerp(double from, double to, double weight) =>
        from + (to - from) * weight;
}

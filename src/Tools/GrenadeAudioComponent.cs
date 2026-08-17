using System;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Objects;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.Tools;

public enum GrenadeAudioCue
{
    None = 0,

    /// <summary>The detonation.</summary>
    Boom = 1,

    /// <summary>A grenade landing hard enough to be heard.</summary>
    Thud = 2,

    /// <summary>The small mechanical cue when the pin comes free.</summary>
    PinPull = 3,

    /// <summary>The continuous fuse layer while at least one grenade is counting down.</summary>
    Fuse = 4,
}

/// <summary>
/// Replacement-ready grenade audio. Existing clean-room synthesized Boom/Thud remain the fallback,
/// while capture polish adds a mechanical pin cue and a quiet fuse loop on a dedicated player so
/// an explosion can never truncate the countdown layer of another live grenade. Owner-authored
/// streams can be assigned later without changing event semantics or gameplay.
/// </summary>
[GlobalClass]
public partial class GrenadeAudioComponent : Node
{
    private const int MixRate = 22_050;

    [Export] public GrenadeComponent Grenades { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;

    // Optional owner-authored replacements. Null preserves the deterministic clean-room fallbacks.
    [Export] public AudioStream? BoomStream { get; set; }
    [Export] public AudioStream? ThudStream { get; set; }
    [Export] public AudioStream? PinPullStream { get; set; }
    [Export] public AudioStream? FuseLoopStream { get; set; }

    private AudioStream _boom = null!;
    private AudioStream _thud = null!;
    private AudioStream _pinPull = null!;
    private AudioStream _fuseLoop = null!;
    private AudioStreamPlayer _fusePlayer = null!;

    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Historical oracle retained for the original Boom/Thud fallback pair. Capture additions expose
    /// their own count so old grenade-feel assertions remain meaningful rather than silently moving.
    /// </summary>
    public int GeneratedStreamCount { get; private set; }
    public int CaptureSupplementalGeneratedStreamCount { get; private set; }
    public int ReplacementReadyCueCount => 4;
    public int PlayCount { get; private set; }
    public int BoomCount { get; private set; }
    public int ThudCount { get; private set; }
    public int PinPullCount { get; private set; }
    public int FuseStartCount { get; private set; }
    public int FuseStopCount { get; private set; }
    public GrenadeAudioCue LastCue { get; private set; }
    public bool IsFuseLoopPlaying => GodotObject.IsInstanceValid(_fusePlayer) && _fusePlayer.Playing;
    public StringName RoutedBus => Player.Bus;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Grenades) || !Grenades.IsInitialized ||
            !GodotObject.IsInstanceValid(Player))
        {
            throw new InvalidOperationException(
                "GrenadeAudioComponent requires an initialized grenade component and one player.");
        }

        Player.Bus = AudioMix.Sfx;
        Player.MaxPolyphony = Math.Max(1, Player.MaxPolyphony);
        _fusePlayer = new AudioStreamPlayer
        {
            Name = "GrenadeFuseLoopPlayer",
            Bus = AudioMix.Sfx,
            MaxPolyphony = 1,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(_fusePlayer);

        AudioStreamWav fallbackBoom = Synthesize(
            seconds: 0.40,
            loop: false,
            (sample, progress) =>
            {
                double sweep = Lerp(120.0, 38.0, Math.Sqrt(progress));
                double body = Math.Sin(Math.Tau * sweep * sample / MixRate);
                double crack = DeterministicNoise(sample * 13 + 7);
                double attack = progress < 0.02 ? progress / 0.02 : 1.0;
                double decay = Math.Pow(1.0 - progress, 2.2);
                return ((body * 0.75) + (crack * 0.55 * Math.Pow(1.0 - progress, 3.0))) *
                       attack * decay * 0.42;
            });
        AudioStreamWav fallbackThud = Synthesize(
            seconds: 0.09,
            loop: false,
            (sample, progress) =>
            {
                double body = Math.Sin(Math.Tau * 82.0 * sample / MixRate);
                double grit = DeterministicNoise(sample * 5 + 91) * 0.30;
                double envelope = Math.Pow(1.0 - progress, 3.0);
                return (body + grit) * envelope * 0.20;
            });
        AudioStreamWav fallbackPin = Synthesize(
            seconds: 0.12,
            loop: false,
            (sample, progress) =>
            {
                double ping = Math.Sin(Math.Tau * Lerp(1700.0, 760.0, progress) * sample / MixRate);
                double scrape = DeterministicNoise(sample * 11 + 113) * 0.22;
                return (ping * 0.72 + scrape) * Math.Pow(1.0 - progress, 3.2) * 0.22;
            });
        AudioStreamWav fallbackFuse = Synthesize(
            seconds: 0.42,
            loop: true,
            (sample, progress) =>
            {
                double hiss = (
                    DeterministicNoise(sample * 3 + 19) +
                    DeterministicNoise(sample * 3 + 18) +
                    DeterministicNoise(sample * 3 + 17)) / 3.0;
                double crackleEnvelope = Math.Pow(Math.Max(0.0, Math.Sin(Math.Tau * 7.0 * progress)), 8.0);
                double crackle = DeterministicNoise(sample * 17 + 331) * crackleEnvelope;
                return (hiss * 0.12 + crackle * 0.24) * 0.26;
            });

        _boom = Valid(BoomStream) ? BoomStream! : fallbackBoom;
        _thud = Valid(ThudStream) ? ThudStream! : fallbackThud;
        _pinPull = Valid(PinPullStream) ? PinPullStream! : fallbackPin;
        _fuseLoop = Valid(FuseLoopStream) ? FuseLoopStream! : fallbackFuse;
        GeneratedStreamCount = 2;
        CaptureSupplementalGeneratedStreamCount = 2;

        Grenades.Detonated += OnDetonated;
        Grenades.GroundContact += OnGroundContact;
        Grenades.PinPulled += OnPinPulled;
        IsInitialized = true;
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(_fusePlayer))
            return;

        bool shouldPlay = HasLiveFuse();
        if (shouldPlay == _fusePlayer.Playing)
            return;

        if (shouldPlay)
        {
            _fusePlayer.VolumeDb = Grenades.Profile.AudioVolumeDb - 9.0f;
            _fusePlayer.Stream = _fuseLoop;
            _fusePlayer.Play();
            FuseStartCount++;
            PlayCount++;
            LastCue = GrenadeAudioCue.Fuse;
        }
        else
        {
            _fusePlayer.Stop();
            FuseStopCount++;
        }
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Grenades))
        {
            Grenades.Detonated -= OnDetonated;
            Grenades.GroundContact -= OnGroundContact;
            Grenades.PinPulled -= OnPinPulled;
        }

        foreach (AudioStreamPlayer player in new[] { Player, _fusePlayer })
        {
            if (GodotObject.IsInstanceValid(player))
            {
                player.Stop();
                player.Stream = null;
            }
        }
    }

    private void OnDetonated(Vector2 _center)
    {
        BoomCount++;
        Play(GrenadeAudioCue.Boom, _boom);
    }

    private void OnGroundContact(float _impactSpeed)
    {
        ThudCount++;
        Play(GrenadeAudioCue.Thud, _thud);
    }

    private void OnPinPulled(Vector2 _point)
    {
        PinPullCount++;
        Play(GrenadeAudioCue.PinPull, _pinPull);
    }

    private bool HasLiveFuse()
    {
        if (!GodotObject.IsInstanceValid(Grenades.Registry))
            return false;

        LooseObjectRegistry registry = Grenades.Registry;
        for (int index = 0; index < LooseObjectRegistry.Capacity; index++)
        {
            LooseObjectBody? body = registry.BodyAt(index);
            if (!GodotObject.IsInstanceValid(body) || body!.SemanticContentId != ContentIds.ToolGrenade)
                continue;

            if (Grenades.TryGetPresentationState(body.RuntimeId, out GrenadePresentationState state) &&
                state.Stage == GrenadeFuseStage.Live && state.FuseTicksRemaining > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void Play(GrenadeAudioCue cue, AudioStream stream)
    {
        Player.VolumeDb = Grenades.Profile.AudioVolumeDb;
        Player.Stream = stream;
        Player.Play();
        LastCue = cue;
        PlayCount++;
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

    private static bool Valid(AudioStream? stream) => stream is not null && GodotObject.IsInstanceValid(stream);

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

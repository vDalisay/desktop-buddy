using System;
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

    [Export] public FireSprayerComponent Sprayer { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;
    [Export] public AudioStreamPlayer LoopPlayer { get; set; } = null!;

    private AudioStreamWav _hiss = null!;
    private AudioStreamWav _ignition = null!;

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

        // Band-limited noise with a slow breath under it: a gas flame, not a hi-hat. The
        // clip is authored to loop cleanly by cross-fading its own tail into its head.
        _hiss = Synthesize(
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
        _ignition = Synthesize(
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
        GeneratedStreamCount = 2;

        Sprayer.SprayingChanged += OnSprayingChanged;
        Sprayer.Ignited += OnIgnited;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Sprayer))
        {
            Sprayer.SprayingChanged -= OnSprayingChanged;
            Sprayer.Ignited -= OnIgnited;
        }

        foreach (AudioStreamPlayer player in new[] { Player, LoopPlayer })
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
            LoopPlayer.VolumeDb = Sprayer.Profile.AudioVolumeDb;
            LoopPlayer.Stream = _hiss;
            LoopPlayer.Play();
            IsHissing = true;
            HissStartCount++;
            PlayCount++;
            LastCue = FireAudioCue.Hiss;
            return;
        }

        LoopPlayer.Stop();
        if (IsHissing)
            HissStopCount++;
        IsHissing = false;
    }

    private void OnIgnited(Vector2 _point)
    {
        Player.VolumeDb = Sprayer.Profile.AudioVolumeDb;
        Player.Stream = _ignition;
        Player.Play();
        IgnitionCueCount++;
        PlayCount++;
        LastCue = FireAudioCue.Ignition;
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

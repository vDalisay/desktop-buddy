using System;
using Godot;

namespace DesktopBuddy.Tools;

public enum GrenadeAudioCue
{
    None = 0,

    /// <summary>The detonation.</summary>
    Boom = 1,

    /// <summary>A grenade landing hard enough to be heard.</summary>
    Thud = 2,
}

/// <summary>
/// Provisional clean-room audio seam for the Grenade, on exactly the
/// <see cref="SwingAudioComponent"/> idiom: two short PCM clips synthesized once at startup
/// and routed through one authored player — no sampled source file, and no audio-server
/// volume mutation.
///
/// <para>The thud's gating lives in <see cref="GrenadeComponent"/> rather than here,
/// because "did this grenade land hard" is a fact about the physics, not about the sound.
/// This component plays what it is told and counts what it played, so a scenario can use
/// the counters as oracles.</para>
/// </summary>
[GlobalClass]
public partial class GrenadeAudioComponent : Node
{
    private const int MixRate = 22_050;

    [Export] public GrenadeComponent Grenades { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;

    private AudioStreamWav _boom = null!;
    private AudioStreamWav _thud = null!;

    public bool IsInitialized { get; private set; }
    public int GeneratedStreamCount { get; private set; }
    public int PlayCount { get; private set; }
    public int BoomCount { get; private set; }
    public int ThudCount { get; private set; }
    public GrenadeAudioCue LastCue { get; private set; }
    public StringName RoutedBus => Player.Bus;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Grenades) || !Grenades.IsInitialized ||
            !GodotObject.IsInstanceValid(Player))
        {
            throw new InvalidOperationException(
                "GrenadeAudioComponent requires an initialized grenade component and one player.");
        }

        // Low burst with a noise tail: a body thump under a long decaying hiss.
        _boom = Synthesize(
            seconds: 0.40,
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
        // Short, dull, and much quieter: a heavy object meeting a floor.
        _thud = Synthesize(
            seconds: 0.09,
            (sample, progress) =>
            {
                double body = Math.Sin(Math.Tau * 82.0 * sample / MixRate);
                double grit = DeterministicNoise(sample * 5 + 91) * 0.30;
                double envelope = Math.Pow(1.0 - progress, 3.0);
                return (body + grit) * envelope * 0.20;
            });
        GeneratedStreamCount = 2;

        Grenades.Detonated += OnDetonated;
        Grenades.GroundContact += OnGroundContact;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Grenades))
        {
            Grenades.Detonated -= OnDetonated;
            Grenades.GroundContact -= OnGroundContact;
        }

        if (GodotObject.IsInstanceValid(Player))
        {
            Player.Stop();
            Player.Stream = null;
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

    private void Play(GrenadeAudioCue cue, AudioStreamWav stream)
    {
        Player.VolumeDb = Grenades.Profile.AudioVolumeDb;
        Player.Stream = stream;
        Player.Play();
        LastCue = cue;
        PlayCount++;
    }

    private static AudioStreamWav Synthesize(
        double seconds,
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

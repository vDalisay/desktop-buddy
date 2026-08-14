using System;
using DesktopBuddy.Interaction;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.Tools;

public enum SwingAudioCue
{
    None = 0,
    ChargeStarted = 1,
    ChargeCompleted = 2,
    SwingReleased = 3,
    HomeRunImpact = 4,
}

/// <summary>
/// Provisional clean-room audio seam for the charged bat. Four short PCM clips
/// are synthesized once at startup and routed through one authored player; no
/// sampled source file or audio-server volume mutation is involved.
/// </summary>
[GlobalClass]
public partial class SwingAudioComponent : Node
{
    private const int MixRate = 22_050;

    [Export] public CursorToolController CursorTools { get; set; } = null!;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;

    private AudioStreamWav _chargeStarted = null!;
    private AudioStreamWav _chargeCompleted = null!;
    private AudioStreamWav _swingReleased = null!;
    private AudioStreamWav _homeRunImpact = null!;

    public bool IsInitialized { get; private set; }
    public int GeneratedStreamCount { get; private set; }
    public int PlayCount { get; private set; }
    public int ChargeStartedCount { get; private set; }
    public int ChargeCompletedCount { get; private set; }
    public int SwingReleasedCount { get; private set; }
    public int HomeRunImpactCount { get; private set; }
    public SwingAudioCue LastCue { get; private set; }
    public StringName RoutedBus => Player.Bus;

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(CursorTools) || !CursorTools.IsInitialized ||
            !GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(Player))
        {
            throw new InvalidOperationException(
                "SwingAudioComponent requires initialized tool/damage components and one player.");
        }

        Player.Bus = AudioMix.Sfx;

        _chargeStarted = Synthesize(
            seconds: 0.11,
            (sample, progress) =>
            {
                double frequency = Lerp(220.0, 440.0, progress);
                double envelope = SmoothAttackDecay(progress, attackFraction: 0.12);
                return Math.Sin(Math.Tau * frequency * sample / MixRate) * envelope * 0.20;
            });
        _chargeCompleted = Synthesize(
            seconds: 0.18,
            (sample, progress) =>
            {
                double envelope = SmoothAttackDecay(progress, attackFraction: 0.06);
                double fundamental = Math.Sin(Math.Tau * 880.0 * sample / MixRate);
                double overtone = Math.Sin(Math.Tau * 1_320.0 * sample / MixRate) * 0.35;
                return (fundamental + overtone) * envelope * 0.18;
            });
        _swingReleased = Synthesize(
            seconds: 0.16,
            (sample, progress) =>
            {
                double frequency = Lerp(720.0, 120.0, progress);
                double tone = Math.Sin(Math.Tau * frequency * sample / MixRate);
                double noise = DeterministicNoise(sample) * 0.45;
                return (tone + noise) * Math.Sin(Math.PI * progress) * 0.17;
            });
        _homeRunImpact = Synthesize(
            seconds: 0.14,
            (sample, progress) =>
            {
                double crack = DeterministicNoise(sample * 17 + 31);
                double body = Math.Sin(Math.Tau * 95.0 * sample / MixRate);
                double envelope = (1.0 - progress) * (1.0 - progress);
                return (crack * 0.72 + body * 0.45) * envelope * 0.28;
            });
        GeneratedStreamCount = 4;

        CursorTools.ChargeStarted += OnChargeStarted;
        CursorTools.ChargeCompleted += OnChargeCompleted;
        CursorTools.SwingReleased += OnSwingReleased;
        Pipeline.ImpactAccepted += OnImpactAccepted;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(CursorTools))
        {
            CursorTools.ChargeStarted -= OnChargeStarted;
            CursorTools.ChargeCompleted -= OnChargeCompleted;
            CursorTools.SwingReleased -= OnSwingReleased;
        }

        if (GodotObject.IsInstanceValid(Pipeline))
        {
            Pipeline.ImpactAccepted -= OnImpactAccepted;
        }

        if (GodotObject.IsInstanceValid(Player))
        {
            Player.Stop();
            Player.Stream = null;
        }
    }

    private void OnChargeStarted()
    {
        if (CursorTools.ActiveProfile?.Swing is not { } profile)
        {
            return;
        }

        ChargeStartedCount++;
        Play(SwingAudioCue.ChargeStarted, _chargeStarted, profile);
    }

    private void OnChargeCompleted()
    {
        if (CursorTools.ActiveProfile?.Swing is not { } profile)
        {
            return;
        }

        ChargeCompletedCount++;
        Play(SwingAudioCue.ChargeCompleted, _chargeCompleted, profile);
    }

    private void OnSwingReleased(float releasedCharge, int swingEpoch)
    {
        if (swingEpoch <= 0 || CursorTools.ActiveProfile?.Swing is not { } profile)
        {
            return;
        }

        SwingReleasedCount++;
        Play(SwingAudioCue.SwingReleased, _swingReleased, profile);
    }

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        if (impact.SwingEpoch <= 0 ||
            CursorTools.SwingProfileForContent(impact.ContentId) is not { } profile)
        {
            return;
        }

        HomeRunImpactCount++;
        Play(SwingAudioCue.HomeRunImpact, _homeRunImpact, profile);
    }

    private void Play(SwingAudioCue cue, AudioStreamWav stream, SwingToolProfile profile)
    {
        Player.VolumeDb = profile.AudioVolumeDb;
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

    private static double SmoothAttackDecay(double progress, double attackFraction)
    {
        if (progress < attackFraction)
        {
            double attack = progress / attackFraction;
            return attack * attack * (3.0 - 2.0 * attack);
        }

        double decay = (progress - attackFraction) / (1.0 - attackFraction);
        return (1.0 - decay) * (1.0 - decay);
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

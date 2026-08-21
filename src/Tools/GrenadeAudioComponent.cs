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
}

/// <summary>
/// Replacement-ready grenade audio. Existing clean-room synthesized Boom/Thud remain the fallback,
/// while capture polish adds a mechanical pin cue. There is deliberately no countdown/fuse
/// layer: the owner cut it 2026-08-19 — a live grenade is silent until it lands or goes off.
/// Owner-authored streams can be assigned without changing event semantics or gameplay.
/// </summary>
[GlobalClass]
public partial class GrenadeAudioComponent : Node
{
    private const int MixRate = 22_050;
    private const int PunctuationPolyphony = 8;

    [Export] public GrenadeComponent Grenades { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;

    // Optional owner-authored replacements. Null preserves the deterministic clean-room fallbacks.
    [Export] public AudioStream? BoomStream { get; set; }
    [Export] public AudioStream? BoomStream2 { get; set; }
    [Export] public AudioStream? ThudStream { get; set; }
    [Export] public AudioStream? ThudStream2 { get; set; }
    [Export] public AudioStream? PinPullStream { get; set; }

    private AudioStream _boom = null!;
    private AudioStream _thud = null!;
    private AudioStream _pinPull = null!;

    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Historical oracle retained for the original Boom/Thud fallback pair. Capture additions expose
    /// their own count so old grenade-feel assertions remain meaningful rather than silently moving.
    /// </summary>
    public int GeneratedStreamCount { get; private set; }
    public int CaptureSupplementalGeneratedStreamCount { get; private set; }
    public int ReplacementReadyCueCount => 3;
    public int PlayCount { get; private set; }
    public int BoomCount { get; private set; }
    public int ThudCount { get; private set; }
    public int PinPullCount { get; private set; }
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

        Player.Bus = AudioMix.Sfx;
        // Pin pulls, thuds and especially staggered detonations are short punctuation cues. The
        // capture branch allows several to coexist so a second grenade cannot audibly erase the
        Player.MaxPolyphony = Math.Max(PunctuationPolyphony, Player.MaxPolyphony);

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
        _boom = SfxRandomizer.Pick(2.0f, BoomStream, BoomStream2) ?? fallbackBoom;
        _thud = SfxRandomizer.Pick(3.0f, ThudStream, ThudStream2) ?? fallbackThud;
        _pinPull = Valid(PinPullStream) ? PinPullStream! : fallbackPin;
        GeneratedStreamCount = 2;
        CaptureSupplementalGeneratedStreamCount = 1;

        Grenades.Detonated += OnDetonated;
        Grenades.GroundContact += OnGroundContact;
        Grenades.PinPulled += OnPinPulled;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Grenades))
        {
            Grenades.Detonated -= OnDetonated;
            Grenades.GroundContact -= OnGroundContact;
            Grenades.PinPulled -= OnPinPulled;
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

    private void OnPinPulled(Vector2 _point)
    {
        PinPullCount++;
        // Knocked out by a bat or a dart rather than drawn by hand: what the player hears is
        // the grenade's own metal, the same take its landings use, over the tool's impact
        // (owner instruction 2026-08-21). The drawn-ring cue is for the deliberate pull.
        if (Grenades.LastPinPullWasStruck)
        {
            ThudCount++;
            Play(GrenadeAudioCue.Thud, _thud);
            return;
        }

        Play(GrenadeAudioCue.PinPull, _pinPull);
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

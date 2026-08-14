using DesktopBuddy.Domain.Content;
using System;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>Plays authored reaction, impact, and loose-object landing cues.</summary>
[GlobalClass]
public partial class ReactionAudioPresenter : Node
{
    private const int MixRate = 22050;
    private const int VoiceCount = 8;
    private const float ImpactRandomPitchSemitones = 2.0f;
    private const float QuietImpactVolumeDb = -12.0f;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public LooseObjectRegistry Objects { get; set; } = null!;
    [Export] public ReactionProfile Profile { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;
    [Export] public AudioStream? BuddyImpact1 { get; set; }
    [Export] public AudioStream? BuddyImpact2 { get; set; }
    [Export] public AudioStream? BuddyHardImpact1 { get; set; }
    [Export] public AudioStream? BuddyHardImpact2 { get; set; }
    [Export] public AudioStream? ItemFalling { get; set; }

    private AudioStream? _buddyImpact;
    private AudioStream? _buddyHardImpact;
    private AudioStream? _itemFalling;
    private float _hardImpactPain;
    private float _maximumPain;
    private float _baseVolumeDb;
    private AudioStreamPlayer[] _voices = Array.Empty<AudioStreamPlayer>();
    private int _nextVoiceIndex;

    public int BuddyImpactCount { get; private set; }
    public int BuddyHardImpactCount { get; private set; }
    public int ItemFallingCount { get; private set; }
    public float HardImpactPainThreshold => _hardImpactPain;
    public StringName RoutedBus => Player.Bus;
    public int VoicePoolSize => _voices.Length;
    public int ActiveVoiceCount
    {
        get
        {
            int count = 0;
            foreach (AudioStreamPlayer voice in _voices)
            {
                if (GodotObject.IsInstanceValid(voice) && voice.Playing)
                    count++;
            }

            return count;
        }
    }

    public AudioStream? LastPlayedStream { get; private set; }
    public float LastPlayedVolumeDb { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !GodotObject.IsInstanceValid(Profile) ||
            !GodotObject.IsInstanceValid(Player))
            throw new InvalidOperationException("ReactionAudioPresenter requires pipeline, profile, and player.");

        _buddyImpact = BuildVariations(BuddyImpact1, BuddyImpact2);
        _buddyHardImpact = BuildVariations(BuddyHardImpact1, BuddyHardImpact2);
        _itemFalling = IsValid(ItemFalling) ? ItemFalling : null;
        _hardImpactPain = HardImpactPainFrom(Pipeline.Profile);
        _maximumPain = MaximumPainFrom(Pipeline.Profile);
        _baseVolumeDb = Player.VolumeDb;
        Player.MaxPolyphony = 1;
        _voices = new AudioStreamPlayer[VoiceCount];
        _voices[0] = Player;
        for (int index = 1; index < _voices.Length; index++)
        {
            var voice = new AudioStreamPlayer
            {
                Name = $"ReactionVoice{index + 1}",
                Bus = Player.Bus,
                ProcessMode = Player.ProcessMode,
                VolumeDb = _baseVolumeDb,
                MaxPolyphony = 1,
            };
            AddChild(voice);
            _voices[index] = voice;
        }

        Pipeline.ImpactAccepted += OnImpact;
        Pipeline.CareAwarded += OnCare;
        if (GodotObject.IsInstanceValid(Objects))
            Objects.Landed += OnObjectLanded;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
        {
            Pipeline.ImpactAccepted -= OnImpact;
            Pipeline.CareAwarded -= OnCare;
        }
        if (GodotObject.IsInstanceValid(Objects))
            Objects.Landed -= OnObjectLanded;
        foreach (AudioStreamPlayer voice in _voices)
        {
            if (GodotObject.IsInstanceValid(voice))
            {
                voice.Stop();
                voice.Stream = null;
            }
        }

        _voices = Array.Empty<AudioStreamPlayer>();
    }

    private void OnImpact(AcceptedImpact impact)
    {
        if (impact.MoodEffect == ImpactMoodEffectKind.Enjoyment)
        {
            PlayChirp(Profile.CareChirpHz, 7_000.0f, _baseVolumeDb);
            return;
        }

        if (impact.ContentId == ContentIds.RoomBoundary)
        {
            if (impact.Pain < _hardImpactPain)
                return;

            BuddyHardImpactCount++;
            PlayImpact(_buddyHardImpact, impact);
            return;
        }

        BuddyImpactCount++;
        PlayImpact(_buddyImpact, impact);
    }

    private void PlayImpact(AudioStream? stream, AcceptedImpact impact)
    {
        if (IsValid(stream))
        {
            PlayStream(stream!, VolumeDbForPain(impact.Pain));
            return;
        }

        bool glove = impact.ContentId == ContentIds.ToolBoxingGlove;
        float normalized = _maximumPain <= 0.0f
            ? 1.0f
            : Mathf.Clamp(impact.Pain / _maximumPain, 0.0f, 1.0f);
        PlayChirp(
            Profile.PainChirpHz * Mathf.Lerp(1.15f, 0.72f, normalized),
            glove ? Profile.GloveImpactAmplitude : 7_000.0f,
            VolumeDbForPain(impact.Pain));
    }

    private void OnObjectLanded(LooseObjectLanding landing)
    {
        // GrenadeComponent owns its distinct, speed-gated metallic landing cue.
        if (landing.ContentId == ContentIds.ToolGrenade || !IsValid(_itemFalling))
            return;

        ItemFallingCount++;
        PlayStream(_itemFalling!, _baseVolumeDb);
    }

    private void OnCare(CareKind kind) => PlayChirp(Profile.CareChirpHz, 7_000.0f, _baseVolumeDb);

    private static AudioStream? BuildVariations(AudioStream? first, AudioStream? second)
    {
        bool firstValid = IsValid(first);
        bool secondValid = IsValid(second);
        if (!firstValid)
            return secondValid ? second : null;
        if (!secondValid)
            return first;

        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitchSemitones = ImpactRandomPitchSemitones,
        };
        randomizer.AddStream(0, first!);
        randomizer.AddStream(1, second!);
        return randomizer;
    }

    private static bool IsValid(AudioStream? stream) =>
        stream is not null && GodotObject.IsInstanceValid(stream);

    private static float HardImpactPainFrom(PainConversionProfile profile)
    {
        float[] anchors = profile.PainAnchors;
        if (anchors is null || anchors.Length == 0)
            return 55.0f;

        // The third pain anchor is the existing curve's hard-impact point; no separate SFX
        // speed threshold is introduced.
        return anchors[Math.Min(2, anchors.Length - 1)];
    }

    private static float MaximumPainFrom(PainConversionProfile profile)
    {
        float[] anchors = profile.PainAnchors;
        if (anchors is null || anchors.Length == 0)
            return 100.0f;

        float maximum = anchors[anchors.Length - 1];
        return float.IsFinite(maximum) && maximum > 0.0f ? maximum : 100.0f;
    }

    private float VolumeDbForPain(float pain)
    {
        float normalized = _maximumPain <= 0.0f
            ? 1.0f
            : Mathf.Clamp(pain / _maximumPain, 0.0f, 1.0f);
        return _baseVolumeDb + Mathf.Lerp(QuietImpactVolumeDb, 0.0f, normalized);
    }

    private void PlayStream(AudioStream stream, float volumeDb)
    {
        AudioStreamPlayer? voice = TakeVoice();
        if (voice is null)
            return;

        voice.VolumeDb = volumeDb;
        voice.Stream = stream;
        voice.Play();
        LastPlayedStream = stream;
        LastPlayedVolumeDb = volumeDb;
    }

    private AudioStreamPlayer? TakeVoice()
    {
        for (int offset = 0; offset < _voices.Length; offset++)
        {
            int index = (_nextVoiceIndex + offset) % _voices.Length;
            AudioStreamPlayer voice = _voices[index];
            if (!GodotObject.IsInstanceValid(voice) || voice.Playing)
                continue;

            _nextVoiceIndex = (index + 1) % _voices.Length;
            return voice;
        }

        return null;
    }

    private void PlayChirp(float frequency, float amplitude, float volumeDb)
    {
        AudioStreamPlayer? voice = TakeVoice();
        if (voice is null)
            return;

        int samples = Math.Max(1, (int)(MixRate * Profile.ChirpSeconds));
        var bytes = new byte[samples * 2];
        for (int i = 0; i < samples; i++)
        {
            double envelope = 1.0 - (double)i / samples;
            short value = (short)(Math.Sin(Math.Tau * frequency * i / MixRate) * envelope * amplitude);
            bytes[i * 2] = (byte)(value & 0xff);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xff);
        }
        voice.VolumeDb = volumeDb;
        voice.Stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = MixRate,
            Stereo = false,
            Data = bytes,
        };
        voice.Play();
        LastPlayedStream = voice.Stream;
        LastPlayedVolumeDb = volumeDb;
    }
}

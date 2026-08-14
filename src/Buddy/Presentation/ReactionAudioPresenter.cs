using DesktopBuddy.Domain.Content;
using System;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Platform;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>Plays authored reaction, impact, landing, and cursor-gun cues.</summary>
[GlobalClass]
public partial class ReactionAudioPresenter : Node
{
    private const int MixRate = 22050;
    private const int VoiceCount = 8;
    private const float ImpactRandomPitchSemitones = 3.5f;
    private const float ImpactBaseVolumeOffsetDb = -6.0f;
    private const float QuietImpactVolumeDb = -12.0f;
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public LooseObjectRegistry Objects { get; set; } = null!;
    [Export] public CursorGunComponent Guns { get; set; } = null!;
    [Export] public ReactionProfile Profile { get; set; } = null!;
    [Export] public AudioStreamPlayer Player { get; set; } = null!;
    [Export] public AudioStream? BuddyImpact1 { get; set; }
    [Export] public AudioStream? BuddyImpact2 { get; set; }
    [Export] public AudioStream? BuddyHardImpact1 { get; set; }
    [Export] public AudioStream? BuddyHardImpact2 { get; set; }
    [Export] public AudioStream? ItemFalling { get; set; }
    [Export] public AudioStream? GloveImpact1 { get; set; }
    [Export] public AudioStream? GloveImpact2 { get; set; }
    [Export] public AudioStream? GloveImpact3 { get; set; }
    [Export] public AudioStream? GloveImpact4 { get; set; }
    [Export] public AudioStream? PistolShot1 { get; set; }
    [Export] public AudioStream? PistolShot2 { get; set; }
    [Export] public AudioStream? PistolReload { get; set; }

    private AudioStream? _buddyImpact;
    private AudioStream? _buddyHardImpact;
    private AudioStream? _itemFalling;
    private AudioStream? _gloveImpact;
    private AudioStream? _pistolShot;
    private AudioStream? _pistolReload;
    private float _hardImpactPain;
    private float _maximumPain;
    private float _baseVolumeDb;
    private float _grabbedBoundaryPainThreshold;
    private AudioStreamPlayer[] _voices = Array.Empty<AudioStreamPlayer>();
    private BuddyPartWallImpactDetector[] _wallImpactDetectors = Array.Empty<BuddyPartWallImpactDetector>();
    private AcceptedImpact?[] _pendingGrabbedWallImpacts = Array.Empty<AcceptedImpact?>();
    private int _nextVoiceIndex;

    public int BuddyImpactCount { get; private set; }
    public int BuddyHardImpactCount { get; private set; }
    public int ItemFallingCount { get; private set; }
    public int GloveImpactCount { get; private set; }
    public int PistolShotCount { get; private set; }
    public int PistolReloadCount { get; private set; }
    public int WallImpactCount { get; private set; }
    public BuddyPart? LastWallImpactPart { get; private set; }
    public AudioStream? LastWallImpactStream { get; private set; }
    public float LastWallImpactVolumeDb { get; private set; }
    public float HardImpactPainThreshold => _hardImpactPain;
    public int WallImpactDetectorCount => _wallImpactDetectors.Length;
    public int WallImpactDetectionCount(BuddyPart part)
    {
        int index = (int)part;
        return (uint)index < (uint)_wallImpactDetectors.Length
            ? _wallImpactDetectors[index].DetectionCount
            : 0;
    }
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
        if (!GodotObject.IsInstanceValid(Pipeline) || !GodotObject.IsInstanceValid(Guns) ||
            !Guns.IsInitialized || !GodotObject.IsInstanceValid(Profile) ||
            !GodotObject.IsInstanceValid(Player))
            throw new InvalidOperationException(
                "ReactionAudioPresenter requires initialized pipeline/guns, profile, and player.");

        _buddyImpact = BuildVariations(BuddyImpact1, BuddyImpact2);
        _buddyHardImpact = BuildVariations(BuddyHardImpact1, BuddyHardImpact2);
        _itemFalling = IsValid(ItemFalling) ? ItemFalling : null;
        _gloveImpact = BuildVariations(GloveImpact1, GloveImpact2, GloveImpact3, GloveImpact4);
        _pistolShot = BuildVariations(PistolShot1, PistolShot2);
        _pistolReload = BuildRandomized(PistolReload, 1.5f);
        _hardImpactPain = HardImpactPainFrom(Pipeline.Profile);
        _maximumPain = MaximumPainFrom(Pipeline.Profile);
        _grabbedBoundaryPainThreshold = GrabbedBoundaryPainFrom(Pipeline.Profile);
        _baseVolumeDb = Player.VolumeDb;
        // Set before the voice pool below copies it.
        Player.Bus = AudioMix.Sfx;
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
        Guns.ShotFired += OnGunShotFired;
        Guns.ReloadStarted += OnGunReloadStarted;
        Pipeline.Grab.Released += OnGrabReleased;
        _pendingGrabbedWallImpacts = new AcceptedImpact?[Enum.GetValues<BuddyPart>().Length];
        _wallImpactDetectors = new BuddyPartWallImpactDetector[Enum.GetValues<BuddyPart>().Length];
        foreach (BuddyPart part in Enum.GetValues<BuddyPart>())
        {
            var detector = new BuddyPartWallImpactDetector
            {
                Name = $"{part}WallImpactDetector",
                Pipeline = Pipeline,
                TargetPart = part,
            };
            detector.ContactDetected += OnWallContact;
            AddChild(detector);
            detector.Initialize();
            _wallImpactDetectors[(int)part] = detector;
        }
        Pipeline.CareAwarded += OnCare;
        if (GodotObject.IsInstanceValid(Objects))
            Objects.Landed += OnObjectLanded;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
        {
            Pipeline.ImpactAccepted -= OnImpact;
            Pipeline.Grab.Released -= OnGrabReleased;
            Pipeline.CareAwarded -= OnCare;
        }
        if (GodotObject.IsInstanceValid(Guns))
        {
            Guns.ShotFired -= OnGunShotFired;
            Guns.ReloadStarted -= OnGunReloadStarted;
        }
        foreach (BuddyPartWallImpactDetector detector in _wallImpactDetectors)
        {
            if (GodotObject.IsInstanceValid(detector))
                detector.ContactDetected -= OnWallContact;
        }
        _wallImpactDetectors = Array.Empty<BuddyPartWallImpactDetector>();
        _pendingGrabbedWallImpacts = Array.Empty<AcceptedImpact?>();
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

    private void OnGunShotFired(GunProfile profile)
    {
        if (profile.ContentId != ContentIds.ToolPistol || !IsValid(_pistolShot))
            return;

        PistolShotCount++;
        PlayStream(_pistolShot!, _baseVolumeDb);
    }

    private void OnGunReloadStarted(GunProfile profile)
    {
        if (profile.ContentId != ContentIds.ToolPistol || !IsValid(_pistolReload))
            return;

        PistolReloadCount++;
        PlayStream(_pistolReload!, _baseVolumeDb);
    }

    private void OnImpact(AcceptedImpact impact)
    {
        if (impact.ContentId == ContentIds.RoomBoundary && impact.IsBuddyGrabbed)
        {
            RememberGrabbedBoundaryImpact(impact);
            return;
        }

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
        if (impact.ContentId == ContentIds.ToolBoxingGlove)
        {
            GloveImpactCount++;
            PlayImpact(IsValid(_gloveImpact) ? _gloveImpact : _buddyImpact, impact);
            return;
        }

        PlayImpact(_buddyImpact, impact);
    }

    private void OnWallContact(AcceptedImpact impact)
    {
        WallImpactCount++;
        LastWallImpactPart = impact.Part;
        PlayImpact(_buddyImpact, impact.Pain);
        LastWallImpactStream = LastPlayedStream;
        LastWallImpactVolumeDb = LastPlayedVolumeDb;
    }

    private void RememberGrabbedBoundaryImpact(AcceptedImpact impact)
    {
        if (impact.Pain < _grabbedBoundaryPainThreshold)
            return;

        int index = (int)impact.Part;
        if ((uint)index < (uint)_pendingGrabbedWallImpacts.Length)
            _pendingGrabbedWallImpacts[index] = impact;
    }

    private void OnGrabReleased(RigidBody2D releasedBody, bool countsAsThrow)
    {
        // One grabbed point can pull the whole puppet into a multi-part slam, so replay
        // every recent part candidate rather than only the body that was held.
        for (int index = 0; index < _pendingGrabbedWallImpacts.Length; index++)
        {
            AcceptedImpact? pending = _pendingGrabbedWallImpacts[index];
            _pendingGrabbedWallImpacts[index] = null;
            if (pending is not { } impact)
                continue;

            // Match the router's episode re-arm window; a held scuff must not become a
            // delayed sound several seconds after the player lets go.
            if (Pipeline.NowSeconds - impact.TimeSeconds >
                DesktopBuddy.Domain.Interaction.ImpactRouter.DefaultReArmSeconds)
                continue;

            WallImpactCount++;
            LastWallImpactPart = impact.Part;
            if (impact.Pain >= _hardImpactPain)
            {
                BuddyHardImpactCount++;
                PlayImpact(_buddyHardImpact, impact);
            }
            else
            {
                PlayImpact(_buddyImpact, impact);
            }

            LastWallImpactStream = LastPlayedStream;
            LastWallImpactVolumeDb = LastPlayedVolumeDb;
        }
    }

    private void PlayImpact(AudioStream? stream, AcceptedImpact impact)
    {
        PlayImpact(stream, impact.Pain, impact.ContentId == ContentIds.ToolBoxingGlove);
    }

    private void PlayImpact(AudioStream? stream, float pain, bool glove = false)
    {
        if (IsValid(stream))
        {
            PlayStream(stream!, VolumeDbForPain(pain));
            return;
        }

        float normalized = _maximumPain <= 0.0f
            ? 1.0f
            : Mathf.Clamp(pain / _maximumPain, 0.0f, 1.0f);
        PlayChirp(
            Profile.PainChirpHz * Mathf.Lerp(1.15f, 0.72f, normalized),
            glove ? Profile.GloveImpactAmplitude : 7_000.0f,
            VolumeDbForPain(pain));
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

    private static AudioStream? BuildVariations(params AudioStream?[] streams)
    {
        AudioStream? firstValid = null;
        int validCount = 0;
        foreach (AudioStream? stream in streams)
        {
            if (!IsValid(stream))
                continue;

            firstValid ??= stream;
            validCount++;
        }

        if (validCount == 0)
            return null;
        if (validCount == 1)
            return firstValid;

        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitchSemitones = ImpactRandomPitchSemitones,
        };
        int variation = 0;
        foreach (AudioStream? stream in streams)
        {
            if (IsValid(stream))
                randomizer.AddStream(variation++, stream!);
        }

        return randomizer;
    }

    private static AudioStream? BuildRandomized(AudioStream? stream, float pitchSemitones)
    {
        if (!IsValid(stream))
            return null;

        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitchSemitones = pitchSemitones,
            RandomVolumeOffsetDb = 0.5f,
        };
        randomizer.AddStream(0, stream!);
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

    private static float GrabbedBoundaryPainFrom(PainConversionProfile profile)
    {
        float[] anchors = profile.PainAnchors;
        if (anchors is null || anchors.Length < 2)
            return 20.0f;

        // The first positive pain anchor is the existing curve's meaningful-hit point:
        // below it, a held contact remains a quiet scuff; above it, a grabbed slam is
        // deferred until release.
        float threshold = anchors[1];
        return float.IsFinite(threshold) && threshold > anchors[0] ? threshold : 20.0f;
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
        return _baseVolumeDb + ImpactBaseVolumeOffsetDb +
            Mathf.Lerp(QuietImpactVolumeDb, 0.0f, normalized);
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

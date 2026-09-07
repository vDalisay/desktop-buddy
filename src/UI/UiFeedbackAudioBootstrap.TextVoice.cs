using Godot;

namespace DesktopBuddy.UI;

public partial class UiFeedbackAudioBootstrap
{
    private const string TutorialVoiceClip = "GuideSound.mp3";

    /// <summary>
    /// 1.42x puts the clip's 440 Hz fundamental at ~616 Hz. That is 20% below the 780 Hz centre of
    /// the band the synthesized chirps used, on the owner's instruction 2026-09-07: the measured
    /// centre read too bright once the authored sample replaced the sine.
    ///
    /// <para>Pitch and rate move together in Godot, so this also sets the blip length: 87 ms at
    /// source becomes ~61 ms here. That is a little longer than one chirp period at the default
    /// cadence (every 3 characters at ~16 ms each), so beats overlap slightly rather than landing
    /// end to end - which reads as a voice running on, not as a drone. Raising this shortens the
    /// blip as well as brightening it.</para>
    /// </summary>
    private const float TutorialVoicePitch = 1.42f;

    /// <summary>Barely-there wobble: a voice that audibly detunes reads as broken, not alive.</summary>
    private const float TutorialVoicePitchJitter = 1.03f;

    /// <summary>
    /// Matched by measurement to the synthesized chirps this replaced, which sat at -40.9 dBFS
    /// (0.045 amplitude played at the pool's -14 dB). The clip is quiet to begin with and the
    /// 1.77x resample costs a further 3.5 dB, leaving it at -34.0 dBFS, so -7 dB lands it on the
    /// old level - about 9 dB under a button click, which is right for something that fires many
    /// times per sentence where a click fires once.
    /// </summary>
    private const float TutorialVoiceVolumeDb = -7.0f;

    private AudioStream? _tutorialTextVoice;

    /// <summary>
    /// Voices one beat of expressive tutorial text: a single authored blip retriggered per few
    /// characters, the way a 90s RPG speaks. It reuses the bootstrap's existing UI-bus voice pool,
    /// so Interface Sounds volume and audio lifecycle remain authoritative. Cadence belongs to the
    /// text presenter; this method only sounds the beat it is told to.
    /// </summary>
    public static void TryPlayTutorialTextVoice(Node context)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return;
        if (context.GetTree().Root.GetNodeOrNull<UiFeedbackAudioBootstrap>(nameof(UiFeedbackAudioBootstrap)) is not { } audio)
            return;

        audio._tutorialTextVoice ??= BuildTutorialTextVoice();
        if (audio._tutorialTextVoice is null)
            return;
        PlayOnPool(
            audio._voices,
            ref audio._nextVoiceIndex,
            audio._tutorialTextVoice,
            TutorialVoicePitch,
            TutorialVoiceVolumeDb);
    }

    /// <summary>
    /// The authored guide blip, pitched into the voice register the owner picked and jittered just
    /// enough that a long sentence never repeats identically.
    ///
    /// <para>Measured from the source clip: fundamental 440 Hz, 87 ms long, peaking at -30.5 dBFS
    /// (the reference UI click peaks at -10.5 dBFS). The constants below follow from those three
    /// numbers rather than from taste, so re-tuning starts by re-measuring.</para>
    /// </summary>
    private static AudioStream? BuildTutorialTextVoice() =>
        LoadUiClip(TutorialVoiceClip, TutorialVoicePitchJitter);
}

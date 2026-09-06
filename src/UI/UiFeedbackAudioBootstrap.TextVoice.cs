using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.UI;

public partial class UiFeedbackAudioBootstrap
{
    private AudioStream? _tutorialTextVoice;

    /// <summary>
    /// Plays one very short synthetic syllable for expressive tutorial text. It reuses the
    /// bootstrap's existing UI-bus voice pool, so Interface Sounds volume and audio lifecycle
    /// remain authoritative. Cadence belongs to the text presenter; this method only voices one beat.
    /// </summary>
    public static void TryPlayTutorialTextVoice(Node context)
    {
        if (!GodotObject.IsInstanceValid(context) || !context.IsInsideTree())
            return;
        if (context.GetTree().Root.GetNodeOrNull<UiFeedbackAudioBootstrap>(nameof(UiFeedbackAudioBootstrap)) is not { } audio)
            return;

        audio._tutorialTextVoice ??= BuildTutorialTextVoice();
        PlayOnPool(audio._voices, ref audio._nextVoiceIndex, audio._tutorialTextVoice);
    }

    /// <summary>
    /// Five close computer-like chirps chosen without immediate repeats. They are deliberately
    /// quieter and shorter than button clicks: dialogue may request many of them in one sentence.
    /// </summary>
    private static AudioStream BuildTutorialTextVoice()
    {
        var voice = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitch = 1.025f,
            RandomVolumeOffsetDb = 0.35f,
        };

        double[] starts = [690.0, 735.0, 780.0, 825.0, 870.0];
        double[] ends = [735.0, 690.0, 825.0, 780.0, 915.0];
        for (int index = 0; index < starts.Length; index++)
            voice.AddStream(index, Tone(0.026, starts[index], ends[index], 0.045));
        return voice;
    }
}

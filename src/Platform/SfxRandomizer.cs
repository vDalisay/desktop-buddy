using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Shared helpers for authored one-shot variation: several takes of the same cue picked at
/// random without repeats, plus a little pitch wobble so a repeated cue never sounds looped.
/// Null/invalid streams are skipped, so a component keeps working with any subset assigned.
/// </summary>
public static class SfxRandomizer
{
    /// <summary>Default wobble for one-shot punctuation, in semitones.</summary>
    public const float DefaultPitchSemitones = 2.5f;

    /// <summary>
    /// One stream that plays a random take each time. Returns null when nothing valid was
    /// given and the single stream itself when only one take exists and no wobble is wanted.
    /// </summary>
    public static AudioStream? Pick(float pitchSemitones, params AudioStream?[] streams)
    {
        var randomizer = new AudioStreamRandomizer
        {
            PlaybackMode = AudioStreamRandomizer.PlaybackModeEnum.RandomNoRepeats,
            RandomPitchSemitones = pitchSemitones,
        };
        int variation = 0;
        foreach (AudioStream? stream in streams)
        {
            if (IsValid(stream))
                randomizer.AddStream(variation++, stream!);
        }

        return variation == 0 ? null : randomizer;
    }

    public static AudioStream? Pick(params AudioStream?[] streams) =>
        Pick(DefaultPitchSemitones, streams);

    public static bool IsValid(AudioStream? stream) =>
        stream is not null && GodotObject.IsInstanceValid(stream);
}

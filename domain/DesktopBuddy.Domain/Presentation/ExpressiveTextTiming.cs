using System;
using System.Globalization;
using System.Text;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>
/// Meaning authored into tutorial text. These values intentionally describe why a phrase is
/// emphasized rather than how it moves; the Godot presenter owns the visual mapping.
/// </summary>
public enum ExpressiveSemanticRole
{
    Input = 0,
    Action = 1,
    Money = 2,
    Impact = 3,
    Playful = 4,
}

/// <summary>
/// Stable semantic tag names used in authored English copy. The renderer may map these to built-in
/// RichTextLabel BBCode effects, but copy never contains visual tags such as wave or shake.
/// </summary>
public static class ExpressiveSemanticTags
{
    public static string Name(ExpressiveSemanticRole role) => role switch
    {
        ExpressiveSemanticRole.Input => "input",
        ExpressiveSemanticRole.Action => "action",
        ExpressiveSemanticRole.Money => "money",
        ExpressiveSemanticRole.Impact => "impact",
        ExpressiveSemanticRole.Playful => "playful",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown expressive semantic role."),
    };

    public static bool TryParse(string? tag, out ExpressiveSemanticRole role)
    {
        role = tag switch
        {
            "input" => ExpressiveSemanticRole.Input,
            "action" => ExpressiveSemanticRole.Action,
            "money" => ExpressiveSemanticRole.Money,
            "impact" => ExpressiveSemanticRole.Impact,
            "playful" => ExpressiveSemanticRole.Playful,
            _ => default,
        };
        return tag is "input" or "action" or "money" or "impact" or "playful";
    }
}

/// <summary>
/// Pure timing policy for the tutorial typewriter. The Godot view owns glyph shaping/reveal;
/// this model only says how long the reveal should wait after a visible text element.
/// </summary>
public readonly record struct ExpressiveRevealTiming
{
    public double BaseElementSeconds { get; }
    public double CommaPauseSeconds { get; }
    public double SentencePauseSeconds { get; }

    public ExpressiveRevealTiming(
        double baseElementSeconds,
        double commaPauseSeconds,
        double sentencePauseSeconds)
    {
        if (!double.IsFinite(baseElementSeconds) || baseElementSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(baseElementSeconds));
        if (!double.IsFinite(commaPauseSeconds) || commaPauseSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(commaPauseSeconds));
        if (!double.IsFinite(sentencePauseSeconds) || sentencePauseSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(sentencePauseSeconds));

        BaseElementSeconds = baseElementSeconds;
        CommaPauseSeconds = commaPauseSeconds;
        SentencePauseSeconds = sentencePauseSeconds;
    }

    public static ExpressiveRevealTiming Default => new(
        baseElementSeconds: 1.0 / 62.0,
        commaPauseSeconds: 0.085,
        sentencePauseSeconds: 0.16);

    /// <summary>
    /// Delay after one already-shaped visible element. Newlines get a sentence-class pause;
    /// whitespace itself adds no extra delay beyond the normal reveal cadence.
    /// </summary>
    public double DelayAfter(string? textElement)
    {
        if (string.IsNullOrEmpty(textElement))
            return BaseElementSeconds;

        Rune rune = FirstRune(textElement);
        double punctuation = rune.Value switch
        {
            ',' or ';' or ':' => CommaPauseSeconds,
            '.' or '!' or '?' or 0x2026 => SentencePauseSeconds,
            '\n' or '\r' => SentencePauseSeconds,
            _ => 0.0,
        };
        return BaseElementSeconds + punctuation;
    }

    private static Rune FirstRune(string text)
    {
        Rune.DecodeFromUtf16(text.AsSpan(), out Rune rune, out _);
        return rune;
    }
}

/// <summary>
/// Stateless speech-chirp cadence. The presenter counts speakable visible elements and calls this
/// after each reveal; punctuation and whitespace never consume a chirp slot.
/// </summary>
public static class ExpressiveVoiceCadence
{
    public const int DefaultEverySpeakableElements = 3;

    public static bool IsSpeakable(string? textElement)
    {
        if (string.IsNullOrEmpty(textElement))
            return false;

        Rune rune = FirstRune(textElement);
        return !Rune.IsWhiteSpace(rune) &&
               Rune.GetUnicodeCategory(rune) is not (
                   UnicodeCategory.ConnectorPunctuation or
                   UnicodeCategory.DashPunctuation or
                   UnicodeCategory.OpenPunctuation or
                   UnicodeCategory.ClosePunctuation or
                   UnicodeCategory.InitialQuotePunctuation or
                   UnicodeCategory.FinalQuotePunctuation or
                   UnicodeCategory.OtherPunctuation);
    }

    /// <summary>
    /// <paramref name="speakableOrdinal"/> is one-based and counts only elements for which
    /// <see cref="IsSpeakable"/> returned true.
    /// </summary>
    public static bool ShouldChirp(
        int speakableOrdinal,
        string? textElement,
        int everySpeakableElements = DefaultEverySpeakableElements)
    {
        if (speakableOrdinal < 1)
            throw new ArgumentOutOfRangeException(nameof(speakableOrdinal));
        if (everySpeakableElements < 1)
            throw new ArgumentOutOfRangeException(nameof(everySpeakableElements));
        return IsSpeakable(textElement) && ((speakableOrdinal - 1) % everySpeakableElements == 0);
    }

    private static Rune FirstRune(string text)
    {
        Rune.DecodeFromUtf16(text.AsSpan(), out Rune rune, out _);
        return rune;
    }
}

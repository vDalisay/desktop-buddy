using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>One literal span of expressive copy and its optional semantic emphasis role.</summary>
public readonly record struct ExpressiveTextRun(string Text, ExpressiveSemanticRole? Role);

/// <summary>
/// Parsed expressive copy. <see cref="PlainText"/> is the exact player-readable text with valid
/// semantic markers removed; malformed/unknown markers remain literal so bad authoring can never
/// make instructions disappear.
/// </summary>
public sealed record ExpressiveTextDocument(
    string PlainText,
    IReadOnlyList<ExpressiveTextRun> Runs);

/// <summary>
/// Tiny, deliberately non-general semantic markup parser. It recognizes only Desktop Buddy's
/// approved emphasis tags and does not execute arbitrary BBCode. Tags may not nest; a malformed
/// known tag is emitted literally as plain text instead of being partially interpreted.
/// </summary>
public static class ExpressiveSemanticMarkup
{
    public static ExpressiveTextDocument Parse(string? source)
    {
        source ??= string.Empty;
        var runs = new List<ExpressiveTextRun>();
        var plain = new StringBuilder(source.Length);
        int cursor = 0;

        while (cursor < source.Length)
        {
            int open = source.IndexOf('[', cursor);
            if (open < 0)
            {
                AddRun(runs, plain, source[cursor..], role: null);
                break;
            }

            if (open > cursor)
                AddRun(runs, plain, source[cursor..open], role: null);

            int closeBracket = source.IndexOf(']', open + 1);
            if (closeBracket < 0)
            {
                AddRun(runs, plain, source[open..], role: null);
                break;
            }

            string tag = source[(open + 1)..closeBracket];
            if (tag.StartsWith("/", StringComparison.Ordinal) ||
                !ExpressiveSemanticTags.TryParse(tag, out ExpressiveSemanticRole role))
            {
                // Unknown/stray closing marker: show it exactly as authored and continue looking
                // for a later valid marker.
                AddRun(runs, plain, source[open..(closeBracket + 1)], role: null);
                cursor = closeBracket + 1;
                continue;
            }

            string closingTag = $"[/{tag}]";
            int close = source.IndexOf(closingTag, closeBracket + 1, StringComparison.Ordinal);
            if (close < 0)
            {
                // A recognized opening marker without its close is one authoring mistake. Keep
                // the entire remainder literal rather than hiding the opening marker only.
                AddRun(runs, plain, source[open..], role: null);
                break;
            }

            string inner = source[(closeBracket + 1)..close];
            if (inner.IndexOf("[", StringComparison.Ordinal) >= 0)
            {
                // Nested/general markup is outside this language. Preserve the whole construct.
                int end = close + closingTag.Length;
                AddRun(runs, plain, source[open..end], role: null);
                cursor = end;
                continue;
            }

            AddRun(runs, plain, inner, role);
            cursor = close + closingTag.Length;
        }

        return new ExpressiveTextDocument(plain.ToString(), runs);
    }

    private static void AddRun(
        List<ExpressiveTextRun> runs,
        StringBuilder plain,
        string text,
        ExpressiveSemanticRole? role)
    {
        if (text.Length == 0)
            return;

        plain.Append(text);
        if (runs.Count > 0 && runs[^1].Role == role)
        {
            ExpressiveTextRun previous = runs[^1];
            runs[^1] = previous with { Text = previous.Text + text };
            return;
        }
        runs.Add(new ExpressiveTextRun(text, role));
    }
}

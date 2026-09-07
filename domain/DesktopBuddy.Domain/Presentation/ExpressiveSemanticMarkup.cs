using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>One literal span of expressive copy and its optional semantic emphasis role.</summary>
public readonly record struct ExpressiveTextRun(string Text, ExpressiveSemanticRole? Role);

/// <summary>
/// Tiny, deliberately non-general semantic markup parser. It recognizes only Desktop Buddy's
/// approved emphasis tags and returns semantic runs directly; there is no message/document/template
/// pipeline. Tags may not nest, and malformed/unknown markers remain literal so bad authoring can
/// never make tutorial instructions disappear or execute arbitrary BBCode.
/// </summary>
public static class ExpressiveSemanticMarkup
{
    public static IReadOnlyList<ExpressiveTextRun> Parse(string? source)
    {
        source ??= string.Empty;
        var runs = new List<ExpressiveTextRun>();
        int cursor = 0;

        while (cursor < source.Length)
        {
            int open = source.IndexOf('[', cursor);
            if (open < 0)
            {
                AddRun(runs, source[cursor..], role: null);
                break;
            }

            if (open > cursor)
                AddRun(runs, source[cursor..open], role: null);

            int closeBracket = source.IndexOf(']', open + 1);
            if (closeBracket < 0)
            {
                AddRun(runs, source[open..], role: null);
                break;
            }

            string tag = source[(open + 1)..closeBracket];
            if (tag.StartsWith("/", StringComparison.Ordinal) ||
                !ExpressiveSemanticTags.TryParse(tag, out ExpressiveSemanticRole role))
            {
                AddRun(runs, source[open..(closeBracket + 1)], role: null);
                cursor = closeBracket + 1;
                continue;
            }

            string closingTag = $"[/{tag}]";
            int close = source.IndexOf(closingTag, closeBracket + 1, StringComparison.Ordinal);
            if (close < 0)
            {
                AddRun(runs, source[open..], role: null);
                break;
            }

            string inner = source[(closeBracket + 1)..close];
            if (inner.IndexOf("[", StringComparison.Ordinal) >= 0)
            {
                int end = close + closingTag.Length;
                AddRun(runs, source[open..end], role: null);
                cursor = end;
                continue;
            }

            AddRun(runs, inner, role);
            cursor = close + closingTag.Length;
        }

        return runs;
    }

    /// <summary>Projects semantic runs back to exactly the player-readable text.</summary>
    public static string PlainText(IReadOnlyList<ExpressiveTextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var plain = new StringBuilder();
        foreach (ExpressiveTextRun run in runs)
            plain.Append(run.Text);
        return plain.ToString();
    }

    private static void AddRun(
        List<ExpressiveTextRun> runs,
        string text,
        ExpressiveSemanticRole? role)
    {
        if (text.Length == 0)
            return;

        if (runs.Count > 0 && runs[^1].Role == role)
        {
            ExpressiveTextRun previous = runs[^1];
            runs[^1] = previous with { Text = previous.Text + text };
            return;
        }
        runs.Add(new ExpressiveTextRun(text, role));
    }
}

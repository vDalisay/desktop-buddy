using System;

namespace DesktopBuddy.Domain.Painting;

/// <summary>
/// What a painting session pays. Both painters — the buddy and the room — snapshot their
/// pixels when the session opens and compare on the way out, so the payout is what the player
/// actually left behind rather than how long they scribbled: painting the same spot a hundred
/// times earns what painting it once earns (owner instruction 2026-08-22).
///
/// <para>Engine-free and pure, so the rate and the ceiling are unit-testable and there is one
/// number to tune rather than two painters that can drift apart.</para>
/// </summary>
public static class PaintSessionPayout
{
    /// <summary>The most one session can pay, whatever was painted.</summary>
    public const long MaximumMilliCredits = 100_000;

    /// <summary>Changed pixels one whole credit is worth.</summary>
    public const long PixelsPerCredit = 8_000;

    private const int BytesPerPixel = 4;

    /// <summary>
    /// How many RGBA pixels differ between two equally sized surfaces. Mismatched lengths
    /// compare only the overlap: a resized surface is a corrupt session, not a jackpot.
    /// </summary>
    public static long ChangedPixels(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        int length = Math.Min(before.Length, after.Length) / BytesPerPixel * BytesPerPixel;
        long changed = 0;
        for (int index = 0; index < length; index += BytesPerPixel)
        {
            if (before[index] != after[index] ||
                before[index + 1] != after[index + 1] ||
                before[index + 2] != after[index + 2] ||
                before[index + 3] != after[index + 3])
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>The session's payout in milli-credits, bounded and never negative.</summary>
    public static long MilliCredits(long changedPixels)
    {
        if (changedPixels <= 0)
            return 0;
        long milli = changedPixels * 1000L / PixelsPerCredit;
        return Math.Min(milli, MaximumMilliCredits);
    }
}

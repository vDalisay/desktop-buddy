using System.Collections.Generic;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// Builds the representative M5 §1.3 session from a seed: about 120 active-interaction
/// minutes plus 89 background minutes, with experimentation, care, misses, duplicate
/// contacts, and short pauses. The seed <b>is</b> the fixture — nothing is committed, so a
/// trace can never drift out of step with the generator that produced it.
///
/// <para>Closed and suspended time simply is not in the trace, which is why no catch-up
/// income can appear in a benchmark run.</para>
/// </summary>
public static class BenchmarkTraceGenerator
{
    public const double ActiveTargetSeconds = 120.0 * 60.0;
    public const double BackgroundTargetSeconds = 89.0 * 60.0;

    /// <summary>
    /// The tools a traced session swings. Which tool is on screen changes harmful memory
    /// and per-tool statistics keys, never the payout, so the trace stays behaviour-only
    /// and is shared unchanged by every purchase strategy.
    /// </summary>
    private static readonly string[] ContactTools =
    {
        ContentIds.ToolBoxingGlove,
        ContentIds.ToolBaseball,
        ContentIds.ToolBaseballBat,
        ContentIds.ToolNerfBlaster,
        ContentIds.ToolPistol,
        ContentIds.ToolSoccerBall,
        ContentIds.ToolGrenade,
        ContentIds.ToolShotgun,
    };

    public static IReadOnlyList<BenchmarkEvent> Generate(int seed)
    {
        var random = new SeededRandomSource((ulong)(uint)seed);
        var events = new List<BenchmarkEvent>();
        double now = 0.0;
        double active = 0.0;
        double background = 0.0;

        // The session opens with the buddy just running: it is launched, then left alone for
        // a minute or two before anyone picks a tool. Without it the first three minutes are
        // the densest of the whole trace and the opening price slots come out inverted.
        double opening = Span(random, 60.0, 180.0, BackgroundTargetSeconds);
        events.Add(new BenchmarkEvent(0.0, BenchmarkEventKind.BackgroundStart, string.Empty, 0.0f, 0));
        now += opening;
        background += opening;

        while (active < ActiveTargetSeconds || background < BackgroundTargetSeconds)
        {
            if (active < ActiveTargetSeconds)
            {
                double length = Span(random, 3.0 * 60.0, 9.0 * 60.0, ActiveTargetSeconds - active);
                events.Add(new BenchmarkEvent(
                    now, BenchmarkEventKind.ActiveStart, string.Empty, 0.0f, 0));
                FillActiveSegment(random, events, now, now + length);
                now += length;
                active += length;
            }

            if (background < BackgroundTargetSeconds)
            {
                double length = Span(random, 2.0 * 60.0, 7.0 * 60.0, BackgroundTargetSeconds - background);
                events.Add(new BenchmarkEvent(
                    now, BenchmarkEventKind.BackgroundStart, string.Empty, 0.0f, 0));
                now += length;
                background += length;
            }
        }

        // The run ends where the last segment ends: a closing marker gives the runner the
        // final span to accrue without inventing an extra event kind.
        events.Add(new BenchmarkEvent(now, BenchmarkEventKind.BackgroundStart, string.Empty, 0.0f, 0));
        return events;
    }

    private static void FillActiveSegment(
        IRandomSource random,
        List<BenchmarkEvent> events,
        double from,
        double to)
    {
        double at = from;
        while (true)
        {
            at += Range(random, 2.0, 8.0);
            if (Roll(random) < 5)
            {
                // A short pause: the player reads something, the buddy is left alone.
                at += Range(random, 20.0, 60.0);
            }

            if (at >= to)
                return;

            int roll = Roll(random);
            if (roll < 8)
            {
                float mood = Roll(random) < 80 ? 1.0f : -1.0f;
                events.Add(new BenchmarkEvent(
                    at, BenchmarkEventKind.Care, ContentIds.ToolPet, mood, 0));
                continue;
            }

            string tool = ContactTools[random.NextInt(0, ContactTools.Length)];
            int part = random.NextInt(0, 6);
            float impulse = Impulse(random);
            events.Add(new BenchmarkEvent(at, BenchmarkEventKind.Contact, tool, impulse, part));

            // A follow-through contact inside the router's re-arm window: the same tool
            // still resting on the same part. It must score nothing.
            if (roll >= 92)
            {
                events.Add(new BenchmarkEvent(
                    at + 0.05, BenchmarkEventKind.Contact, tool, impulse, part));
            }
        }
    }

    /// <summary>A miss, an ordinary hit, or a good one — in roughly that proportion.</summary>
    private static float Impulse(IRandomSource random)
    {
        int roll = Roll(random);
        if (roll < 15)
            return (float)Range(random, 180.0, 349.0);
        return roll < 70
            ? (float)Range(random, 350.0, 1500.0)
            : (float)Range(random, 1500.0, 3200.0);
    }

    private static double Span(IRandomSource random, double minimum, double maximum, double remaining)
    {
        double length = Range(random, minimum, maximum);
        return length < remaining ? length : remaining;
    }

    private static double Range(IRandomSource random, double minimum, double maximum) =>
        minimum + (maximum - minimum) * (random.NextInt(0, 10000) / 10000.0);

    private static int Roll(IRandomSource random) => random.NextInt(0, 100);
}

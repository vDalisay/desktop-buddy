using System;

namespace DesktopBuddy.Domain.Autonomy;

/// <summary>
/// Small, platform-stable SplitMix64 stream used by seeded tests and autonomy.
/// It deliberately avoids <see cref="Random"/> so framework changes cannot
/// silently alter committed scenario decision traces.
/// </summary>
public sealed class SeededRandomSource : IRandomSource
{
    private const ulong Increment = 0x9E3779B97F4A7C15UL;
    private ulong _state;

    public SeededRandomSource(ulong seed)
    {
        _state = seed;
    }

    public int NextInt(int minimumInclusive, int maximumExclusive)
    {
        if (maximumExclusive <= minimumInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumExclusive),
                maximumExclusive,
                "Maximum must be greater than minimum.");
        }

        uint range = checked((uint)(maximumExclusive - minimumInclusive));
        ulong sample = NextUInt64();
        return minimumInclusive + (int)(sample % range);
    }

    private ulong NextUInt64()
    {
        ulong value = (_state += Increment);
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}

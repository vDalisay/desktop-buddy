using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// Stable 64-bit fingerprints (FNV-1a over an invariant-culture rendering) of the two
/// things a report depends on: the priced catalogue plus economy tuning, and the trace.
/// A price edit must move the economy fingerprint; a different seed must move only the
/// trace one. The framework string hash is randomized per process and cannot be used.
/// </summary>
public static class BenchmarkFingerprint
{
    /// <summary>Impulses the pain curve is sampled at, so curve edits show up as changes.</summary>
    private const int CurveSampleStep = 100;
    private const int CurveSampleMax = 3500;

    public static string OfEconomy(ToolCatalogue catalogue, BenchmarkEconomy economy)
    {
        var text = new StringBuilder();
        foreach (CatalogueEntry entry in catalogue.Entries)
        {
            text.Append(entry.ContentId).Append('|')
                .Append(entry.PriceMilliCredits.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(entry.ProgressionOrder.ToString(CultureInfo.InvariantCulture)).Append(';');
        }

        text.Append(economy.CashPerPain.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(economy.MinimumImpulse.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(economy.PassiveCreditsPerSecond.ToString("R", CultureInfo.InvariantCulture))
            .Append(';');

        for (int impulse = 0; impulse <= CurveSampleMax; impulse += CurveSampleStep)
        {
            text.Append(economy.PainCurve.PainFor(impulse).ToString("R", CultureInfo.InvariantCulture))
                .Append(',');
        }

        return Hash(text.ToString());
    }

    public static string OfTrace(IReadOnlyList<BenchmarkEvent> trace)
    {
        var text = new StringBuilder();
        foreach (BenchmarkEvent traced in trace)
        {
            text.Append(traced.AtSeconds.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append((int)traced.Kind).Append('|')
                .Append(traced.ContentId).Append('|')
                .Append(traced.Magnitude.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(traced.BodyRegion).Append(';');
        }

        return Hash(text.ToString());
    }

    private static string Hash(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char character in text)
            {
                hash = (hash ^ character) * 1099511628211UL;
            }

            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}

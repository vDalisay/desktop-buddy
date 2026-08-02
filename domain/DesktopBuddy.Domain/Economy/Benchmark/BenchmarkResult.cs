using System.Collections.Generic;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>One completed purchase, at the cumulative running second it happened.</summary>
public readonly record struct BenchmarkPurchase(
    string ContentId,
    double AtSeconds,
    long PriceMilliCredits);

/// <summary>
/// Everything one benchmark run measured — nothing the report does not print. Income is
/// split by source so the "active dominates passive" obligation is a subtraction rather
/// than an estimate.
/// </summary>
public sealed record BenchmarkResult(
    int Seed,
    string StrategyId,
    double RunningSeconds,
    double ActiveSeconds,
    double BackgroundSeconds,
    long ActiveIncomeMilliCredits,
    long PassiveIncomeMilliCredits,
    int DuplicateContactRejections,
    IReadOnlyList<BenchmarkPurchase> Purchases,
    long EndingBalanceMilliCredits,
    IReadOnlyList<string> EndingOwnership,
    long LargestSingleEventMilliCredits)
{
    public long TotalIncomeMilliCredits =>
        ActiveIncomeMilliCredits + PassiveIncomeMilliCredits;
}

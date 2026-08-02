using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy.Benchmark;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Economy;

/// <summary>
/// The benchmark runner against a tiny hand-built trace and synthetic tuning — never the
/// shipped catalogue, whose prices are calibration output and move without notice.
/// </summary>
public sealed class EconomyBenchmarkTests
{
    private const string Cheap = ContentIds.ToolBaseball;
    private const string Dear = ContentIds.ToolShotgun;

    // cashPerPain = 1 keeps a 3000-impulse head hit worth exactly 120 credits.
    private static BenchmarkEconomy Economy(double passiveCreditsPerSecond = 0.0) => new(
        new PainCurve(new[] { new PainAnchor(350f, 0f), new PainAnchor(3000f, 100f) }),
        1.0,
        350f,
        passiveCreditsPerSecond);

    private static ToolCatalogue Catalogue(long cheapCredits = 10, long dearCredits = 10_000)
    {
        var entries = new List<CatalogueEntry>
        {
            new(ContentIds.ToolGrab, CatalogueEntryKind.StartingTool, 0, 0, true, "n", "d"),
            new(ContentIds.ToolPet, CatalogueEntryKind.StartingTool, 0, 1, true, "n", "d"),
            new(ContentIds.ToolTickle, CatalogueEntryKind.StartingTool, 0, 2, true, "n", "d"),
            new(ContentIds.ToolBoxingGlove, CatalogueEntryKind.StartingTool, 0, 3, true, "n", "d"),
            new(Cheap, CatalogueEntryKind.PurchasableTool, cheapCredits * 1000, 4, true, "n", "d"),
            new(Dear, CatalogueEntryKind.PurchasableTool, dearCredits * 1000, 5, true, "n", "d"),
        };

        return new ToolCatalogue(entries);
    }

    private static BenchmarkStrategy Strategy(params string[] order) => new("test", order);

    /// <summary>Five events: four head contacts (one of them a suppressed repeat) and a care award.</summary>
    private static IReadOnlyList<BenchmarkEvent> Trace() => new[]
    {
        new BenchmarkEvent(0.0, BenchmarkEventKind.ActiveStart, string.Empty, 0f, 0),
        new BenchmarkEvent(1.0, BenchmarkEventKind.Contact, ContentIds.ToolBoxingGlove, 3000f, 0),
        new BenchmarkEvent(1.05, BenchmarkEventKind.Contact, ContentIds.ToolBoxingGlove, 3000f, 0),
        new BenchmarkEvent(2.0, BenchmarkEventKind.Care, ContentIds.ToolPet, 1.0f, 0),
        new BenchmarkEvent(3.0, BenchmarkEventKind.Contact, ContentIds.ToolBoxingGlove, 3000f, 0),
    };

    [Fact]
    public void Run_InsufficientFundsLeavesTheBalanceAloneAndKeepsGoing()
    {
        BenchmarkResult result = EconomyBenchmark.Run(
            Trace(), Strategy(Dear), Catalogue(), Economy());

        Assert.Empty(result.Purchases);
        Assert.False(ContainsPurchase(result, Dear));
        Assert.Equal(result.ActiveIncomeMilliCredits, result.EndingBalanceMilliCredits);
        Assert.Equal(240_000, result.ActiveIncomeMilliCredits);
    }

    [Fact]
    public void Run_AffordableEntryIsBoughtOnceAtItsCataloguePrice()
    {
        BenchmarkResult result = EconomyBenchmark.Run(
            Trace(), Strategy(Cheap), Catalogue(cheapCredits: 100), Economy());

        BenchmarkPurchase purchase = Assert.Single(result.Purchases);
        Assert.Equal(Cheap, purchase.ContentId);
        Assert.Equal(100_000, purchase.PriceMilliCredits);
        Assert.Equal(240_000 - 100_000, result.EndingBalanceMilliCredits);
        Assert.Contains(Cheap, result.EndingOwnership);
    }

    [Fact]
    public void Run_AnAlreadyOwnedEntryIsNeverChargedTwice()
    {
        // The cheap entry is listed twice: the runner must still buy it exactly once.
        BenchmarkResult result = EconomyBenchmark.Run(
            Trace(), Strategy(Cheap, Cheap), Catalogue(cheapCredits: 100), Economy());

        Assert.Single(result.Purchases);
        Assert.Equal(140_000, result.EndingBalanceMilliCredits);
    }

    [Fact]
    public void Run_DuplicateContactScoresNothingAndIsCounted()
    {
        BenchmarkResult result = EconomyBenchmark.Run(
            Trace(), Strategy(), Catalogue(), Economy());

        // Three offered contacts, one of them inside the re-arm window: two payouts.
        Assert.Equal(1, result.DuplicateContactRejections);
        Assert.Equal(240_000, result.ActiveIncomeMilliCredits);
        Assert.Equal(120_000, result.LargestSingleEventMilliCredits);
    }

    [Fact]
    public void Run_PassiveIncomeAccruesOverTheTraceAndIsReportedSeparately()
    {
        BenchmarkResult result = EconomyBenchmark.Run(
            Trace(), Strategy(), Catalogue(), Economy(passiveCreditsPerSecond: 1.0));

        Assert.Equal(3.0, result.RunningSeconds);
        Assert.Equal(3.0, result.ActiveSeconds);
        Assert.Equal(0.0, result.BackgroundSeconds);
        Assert.True(result.PassiveIncomeMilliCredits > 0);
        Assert.Equal(
            result.ActiveIncomeMilliCredits + result.PassiveIncomeMilliCredits,
            result.EndingBalanceMilliCredits);
    }

    [Fact]
    public void Run_IsDeterministicForIdenticalInputs()
    {
        ToolCatalogue catalogue = Catalogue(cheapCredits: 100);
        BenchmarkReportInput First() => Report(catalogue);

        Assert.Equal(BenchmarkReport.Json(First()), BenchmarkReport.Json(First()));
    }

    private static BenchmarkReportInput Report(ToolCatalogue catalogue)
    {
        var results = new List<BenchmarkResult>
        {
            EconomyBenchmark.Run(Trace(), Strategy(Cheap), catalogue, Economy(1.0), 1),
        };

        return new BenchmarkReportInput(
            BenchmarkFingerprint.OfEconomy(catalogue, Economy(1.0)),
            new[] { new KeyValuePair<int, string>(1, BenchmarkFingerprint.OfTrace(Trace())) },
            results,
            new[] { new ObligationCheck("probe", true, "n/a") },
            catalogue);
    }

    [Fact]
    public void Generate_IsSeedStableAndSeedSensitive()
    {
        Assert.Equal(
            BenchmarkFingerprint.OfTrace(BenchmarkTraceGenerator.Generate(1)),
            BenchmarkFingerprint.OfTrace(BenchmarkTraceGenerator.Generate(1)));
        Assert.NotEqual(
            BenchmarkFingerprint.OfTrace(BenchmarkTraceGenerator.Generate(1)),
            BenchmarkFingerprint.OfTrace(BenchmarkTraceGenerator.Generate(2)));
    }

    [Fact]
    public void Generate_ProducesTheRepresentativeSessionShape()
    {
        BenchmarkResult result = EconomyBenchmark.Run(
            BenchmarkTraceGenerator.Generate(1), Strategy(), Catalogue(), Economy());

        Assert.Equal(209.0 * 60.0, result.RunningSeconds, 1.0);
        Assert.Equal(120.0 * 60.0, result.ActiveSeconds, 1.0);
        Assert.Equal(89.0 * 60.0, result.BackgroundSeconds, 1.0);
        // Misses and follow-through contacts are both present, or the trace is not
        // exercising the router at all.
        Assert.True(result.DuplicateContactRejections > 0);
    }

    [Fact]
    public void Schedule_PairsTheTwelvePurchasablesWithTheOwnerLockedTargets()
    {
        Assert.Equal(12, BenchmarkSchedule.Targets.Count);
        Assert.Equal(ContentIds.ToolBaseball, BenchmarkSchedule.Targets[0].ContentId);
        Assert.Equal(3.0, BenchmarkSchedule.Targets[0].TargetMinutes);
        Assert.Equal(ContentIds.ToolDrink, BenchmarkSchedule.Targets[^1].ContentId);
        Assert.Equal(209.0, BenchmarkSchedule.Targets[^1].TargetMinutes);
    }

    [Fact]
    public void Strategies_AreSevenAndBuyOnlyLaunchCatalogueEntries()
    {
        Assert.Equal(7, BenchmarkStrategies.All.Count);
        foreach (BenchmarkStrategy strategy in BenchmarkStrategies.All)
        {
            Assert.NotEmpty(strategy.PurchaseOrder);
            foreach (string contentId in strategy.PurchaseOrder)
                Assert.Contains(contentId, BenchmarkSchedule.PurchasableOrder);
        }
    }

    private static bool ContainsPurchase(BenchmarkResult result, string contentId)
    {
        foreach (BenchmarkPurchase purchase in result.Purchases)
        {
            if (purchase.ContentId == contentId)
                return true;
        }

        return false;
    }
}

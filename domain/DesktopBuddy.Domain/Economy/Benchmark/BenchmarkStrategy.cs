using System.Collections.Generic;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// One player's buying intent, as data: the runner always saves for the first entry of
/// <see cref="PurchaseOrder"/> it does not own yet, and never skips ahead to a cheaper
/// later one. There is deliberately no <c>switch</c> on <see cref="Id"/> anywhere — adding
/// a strategy is adding a list, not a branch.
/// </summary>
public sealed record BenchmarkStrategy(string Id, IReadOnlyList<string> PurchaseOrder);

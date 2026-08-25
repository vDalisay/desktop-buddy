using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Economy.Benchmark;

/// <summary>
/// Resolves the benchmark purchase order from the same validated catalogue projection the
/// running shop uses. The old M5 benchmark hard-coded a 209-minute eleven-item schedule; that
/// stopped representing the game once the Demo shop was repriced and expanded. Benchmark code
/// must therefore consume the authored catalogue instead of owning a second progression table.
/// </summary>
public static class BenchmarkSchedule
{
    /// <summary>Current visible purchasable tools, in their authored shop/progression order.</summary>
    public static IReadOnlyList<string> PurchaseOrder(ToolCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        IReadOnlyList<CatalogueEntry> entries = CataloguePolicy.ShopEntries(catalogue);
        var order = new List<string>(entries.Count);
        foreach (CatalogueEntry entry in entries)
            order.Add(entry.ContentId);
        return order;
    }
}

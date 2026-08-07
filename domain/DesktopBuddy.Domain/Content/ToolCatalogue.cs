using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Economy;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// An immutable, validated snapshot of the project content catalogue. Historical naming is
/// retained because gameplay tool code already depends on this type; cosmetics deliberately
/// reuse the same commerce/ownership boundary instead of introducing a second ledger stack.
/// </summary>
public sealed class ToolCatalogue
{
    private readonly Dictionary<string, CatalogueEntry> _byId;
    private readonly CatalogueEntry[] _ordered;

    public ToolCatalogue(IEnumerable<CatalogueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = new List<CatalogueEntry>(entries);
        IReadOnlyList<string> errors = Validate(list);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Invalid catalogue: {string.Join("; ", errors)}",
                nameof(entries));
        }

        list.Sort(static (left, right) =>
        {
            int byOrder = left.ProgressionOrder.CompareTo(right.ProgressionOrder);
            return byOrder != 0
                ? byOrder
                : string.CompareOrdinal(left.ContentId, right.ContentId);
        });

        _ordered = list.ToArray();
        _byId = new Dictionary<string, CatalogueEntry>(_ordered.Length, StringComparer.Ordinal);
        foreach (CatalogueEntry entry in _ordered)
            _byId[entry.ContentId] = entry;
    }

    public IReadOnlyList<CatalogueEntry> Entries => _ordered;
    public int Count => _ordered.Length;

    public bool TryGet(string? contentId, out CatalogueEntry entry)
    {
        if (contentId is null)
        {
            entry = default;
            return false;
        }
        return _byId.TryGetValue(contentId, out entry);
    }

    public bool Contains(string? contentId) => TryGet(contentId, out _);

    public static IReadOnlyList<string> Validate(IReadOnlyList<CatalogueEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenOrders = new HashSet<int>();

        foreach (CatalogueEntry entry in entries)
        {
            string id = entry.ContentId;
            string label = string.IsNullOrWhiteSpace(id) ? "<empty>" : id;

            if (string.IsNullOrWhiteSpace(id))
                errors.Add("an entry has no content ID");
            else if (!ContentIds.IsCatalogueEntry(id))
                errors.Add($"'{label}' is not a catalogue content ID known to this build");
            else if (!seenIds.Add(id))
                errors.Add($"'{label}' is declared more than once");

            if (entry.ProgressionOrder < 0)
                errors.Add($"'{label}' has a negative progression order");
            else if (!seenOrders.Add(entry.ProgressionOrder))
                errors.Add($"'{label}' reuses progression order {entry.ProgressionOrder}");

            if (string.IsNullOrWhiteSpace(entry.NameKey))
                errors.Add($"'{label}' has no name translation key");
            if (string.IsNullOrWhiteSpace(entry.DescriptionKey))
                errors.Add($"'{label}' has no description translation key");

            bool isTool = ContentIds.IsTool(id);
            bool isCosmetic = ContentIds.IsCosmetic(id);
            if (entry.Kind == CatalogueEntryKind.PassiveUpgrade && isTool)
                errors.Add($"'{label}' is a passive upgrade but is also a selectable tool ID");
            if (entry.Kind == CatalogueEntryKind.Cosmetic && !isCosmetic)
                errors.Add($"'{label}' is a cosmetic entry but does not use the cosmetic namespace");
            if (entry.Kind != CatalogueEntryKind.Cosmetic && isCosmetic)
                errors.Add($"'{label}' uses the cosmetic namespace but is not a cosmetic entry");
            if (entry.IsSelectable && !isTool && !string.IsNullOrWhiteSpace(id))
                errors.Add($"'{label}' is selectable but is not a tool ID");

            if (entry.PriceMilliCredits < 0)
                errors.Add($"'{label}' has a negative price");
            else if (entry.PriceMilliCredits % RewardLedger.MilliCreditsPerCredit != 0)
                errors.Add($"'{label}' is priced in part credits ({entry.PriceMilliCredits} milli)");

            if (entry.IsStarting)
            {
                if (entry.PriceMilliCredits != 0)
                    errors.Add($"'{label}' is a starting entry and cannot carry a price");
                if (!entry.Visible)
                    errors.Add($"'{label}' is a starting entry and cannot be hidden");
            }
            else if (entry.Visible && !entry.HasValidPrice)
            {
                errors.Add($"'{label}' is visible but has no calibrated price");
            }
        }

        return errors;
    }
}

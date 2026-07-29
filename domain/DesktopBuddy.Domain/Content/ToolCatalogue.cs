using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Economy;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// An immutable, validated snapshot of the FR-013 catalogue. The authored Resources are
/// the static-data author (ARCHITECTURE §6); this type is what the rules run against, so
/// the domain never becomes a second place where prices or ordering are declared.
///
/// <para>
/// Construction is total: a catalogue either satisfies every structural rule or it does
/// not exist. <see cref="Validate"/> exposes the same rules as readable messages so the
/// Godot startup validator can report every problem at once instead of throwing on the
/// first one.
/// </para>
/// </summary>
public sealed class ToolCatalogue
{
    private readonly Dictionary<string, CatalogueEntry> _byId;
    private readonly CatalogueEntry[] _ordered;

    /// <summary>Builds the snapshot, throwing when any structural rule is violated.</summary>
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
        {
            _byId[entry.ContentId] = entry;
        }
    }

    /// <summary>Every entry, in progression order (FR-013.4).</summary>
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

    /// <summary>
    /// The structural rules, as messages. Empty means valid. Rules that depend on a
    /// specific milestone's content (which entries must exist, which are finished) live in
    /// <see cref="CataloguePolicy"/>; these are the ones a catalogue can never break.
    /// </summary>
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
            {
                errors.Add("an entry has no content ID");
            }
            else if (!ContentIds.IsCatalogueEntry(id))
            {
                errors.Add($"'{label}' is not a catalogue content ID known to this build");
            }
            else if (!seenIds.Add(id))
            {
                errors.Add($"'{label}' is declared more than once");
            }

            if (entry.ProgressionOrder < 0)
            {
                errors.Add($"'{label}' has a negative progression order");
            }
            else if (!seenOrders.Add(entry.ProgressionOrder))
            {
                errors.Add($"'{label}' reuses progression order {entry.ProgressionOrder}");
            }

            if (string.IsNullOrWhiteSpace(entry.NameKey))
                errors.Add($"'{label}' has no name translation key");
            if (string.IsNullOrWhiteSpace(entry.DescriptionKey))
                errors.Add($"'{label}' has no description translation key");

            // A passive upgrade must not be expressible as a tool at all (FR-019): the
            // string vocabulary, not a runtime filter, keeps it out of selection.
            bool isTool = ContentIds.IsTool(id);
            if (entry.Kind == CatalogueEntryKind.PassiveUpgrade && isTool)
                errors.Add($"'{label}' is a passive upgrade but is also a selectable tool ID");
            if (entry.Kind != CatalogueEntryKind.PassiveUpgrade &&
                !isTool &&
                !string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"'{label}' is selectable but is not a tool ID");
            }

            if (entry.PriceMilliCredits < 0)
            {
                errors.Add($"'{label}' has a negative price");
            }
            else if (entry.PriceMilliCredits % RewardLedger.MilliCreditsPerCredit != 0)
            {
                errors.Add($"'{label}' is priced in part credits ({entry.PriceMilliCredits} milli)");
            }

            if (entry.IsStarting)
            {
                if (entry.PriceMilliCredits != 0)
                    errors.Add($"'{label}' is a starting entry and cannot carry a price");
                if (!entry.Visible)
                    errors.Add($"'{label}' is a starting entry and cannot be hidden");
            }
            else if (entry.Visible && !entry.HasValidPrice)
            {
                // Uncalibrated prices are allowed only while an entry is still invisible;
                // a shown entry is a shippable one and must be buyable.
                errors.Add($"'{label}' is visible but has no calibrated price");
            }
        }

        return errors;
    }
}

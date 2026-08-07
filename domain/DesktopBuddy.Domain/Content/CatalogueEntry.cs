using DesktopBuddy.Domain.Economy;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// What one catalogue entry is, structurally (FR-013.2). The kind decides the rules that
/// apply to it — never the entry's name, ordinal, or presentation.
/// </summary>
public enum CatalogueEntryKind
{
    /// <summary>Owned on every new save and never sold (FR-013.1).</summary>
    StartingTool,

    /// <summary>A purchasable entry that enters tool selection once owned.</summary>
    PurchasableTool,

    /// <summary>A purchasable tool that acts through the care/consume machinery.</summary>
    CareConsumable,

    /// <summary>
    /// A purchasable entry that is never selectable as a tool. It changes existing
    /// behavior instead of adding a tool.
    /// </summary>
    PassiveUpgrade,

    /// <summary>
    /// A permanent appearance unlock. Cosmetics use the existing purchase/ownership
    /// ledger but never enter tool selection.
    /// </summary>
    Cosmetic,
}

/// <summary>
/// One immutable catalogue entry as the domain sees it. The authored Resource keeps the
/// engine-side references (icon, scenes, profiles); this snapshot carries only what the
/// rules need, so catalogue logic stays engine-free (ARCHITECTURE §6, NFR-006.2).
///
/// <para>
/// <see cref="Visible"/> is the "no unfinished shop entry is shown" flag (owner rule,
/// 2026-07-28): an entry stays invisible — and therefore unbuyable — until its slice has
/// passed its automated gates, real-input verification, and the owner's feel review.
/// </para>
/// </summary>
public readonly record struct CatalogueEntry(
    string ContentId,
    CatalogueEntryKind Kind,
    long PriceMilliCredits,
    int ProgressionOrder,
    bool Visible,
    string NameKey,
    string DescriptionKey)
{
    /// <summary>Starting entries are owned from the first save and are never sold.</summary>
    public bool IsStarting => Kind == CatalogueEntryKind.StartingTool;

    /// <summary>Passive upgrades and cosmetics never enter gameplay tool selection.</summary>
    public bool IsSelectable => Kind is not (CatalogueEntryKind.PassiveUpgrade or CatalogueEntryKind.Cosmetic);

    /// <summary>A price the ledger can spend: positive and a whole displayed credit.</summary>
    public bool HasValidPrice =>
        PriceMilliCredits > 0 &&
        PriceMilliCredits % RewardLedger.MilliCreditsPerCredit == 0;
}
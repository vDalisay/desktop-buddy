namespace DesktopBuddy.Domain.Economy;

/// <summary>
/// Stable outcomes returned by the M5 purchase boundary. The shop renders these results;
/// it never edits balances or ownership directly (ARCHITECTURE §11).
/// </summary>
public enum PurchaseStatus
{
    Purchased,
    AlreadyOwned,
    InsufficientFunds,

    /// <summary>The ID is not in this build's catalogue at all.</summary>
    InvalidContentId,

    /// <summary>The catalogue entry carries no spendable whole-credit price.</summary>
    InvalidPrice,

    /// <summary>
    /// The entry exists but is not shown yet: its slice has not passed its gates, so it
    /// cannot be sold (owner rule, 2026-07-28).
    /// </summary>
    NotAvailable,

    /// <summary>The entry is never sold — a starting tool the save already owns.</summary>
    NotPurchasable,
}

/// <summary>
/// Immutable result of one purchase attempt. Failed attempts always report the unchanged
/// post-attempt balance and never partially spend or unlock content.
/// </summary>
public readonly record struct PurchaseResult(
    PurchaseStatus Status,
    string ContentId,
    long PriceMilliCredits,
    long BalanceMilliCredits)
{
    public bool Succeeded => Status == PurchaseStatus.Purchased;
}

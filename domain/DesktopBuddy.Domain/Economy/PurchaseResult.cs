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
    InvalidContentId,
    InvalidPrice,
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

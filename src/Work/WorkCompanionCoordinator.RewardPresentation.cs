namespace DesktopBuddy.Work;

public partial class WorkCompanionCoordinator
{
    /// <summary>
    /// Read-only capture-presentation seam. Settlement remains owned by the existing coordinator
    /// flow; Work Mode UI may observe the amount that has already been transactionally paid this
    /// session, but cannot award or claim anything through this property.
    /// </summary>
    internal long SessionSettledMilliCreditsForPresentation => _sessionSettledMilliCredits;
}

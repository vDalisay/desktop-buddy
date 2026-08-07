using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Work;

public static class WorkMilestoneDefaults
{
    // Reward values are deliberately centralized provisional tuning. The owner locked the
    // thresholds but not payout amounts; economy calibration may change these constants
    // without touching counting/session code.
    public static WorkMilestoneCatalogue Create() => new([
        new WorkMilestoneDefinition(
            "work.session.actions.10000",
            WorkCounterKind.TotalActions,
            WorkMilestoneScope.CurrentSession,
            10_000,
            50 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.RepeatPerSession),
        new WorkMilestoneDefinition(
            "work.session.keyboard.10000",
            WorkCounterKind.KeyboardPresses,
            WorkMilestoneScope.CurrentSession,
            10_000,
            50 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.RepeatPerSession),
        new WorkMilestoneDefinition(
            "work.lifetime.actions.1000000",
            WorkCounterKind.TotalActions,
            WorkMilestoneScope.Lifetime,
            1_000_000,
            1_000 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.OnceLifetime),
    ]);
}
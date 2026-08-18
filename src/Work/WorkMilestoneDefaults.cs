using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Work;

public static class WorkMilestoneDefaults
{
    // Demo progression adds a readable ladder rather than making the first visible Work reward
    // wait for ten thousand actions. Rewards are provisional economy tuning; the stable IDs,
    // scopes and thresholds are the important persistence/platform seams. DEMO-9 may retune
    // reward amounts without changing claimed milestone identity.
    public static WorkMilestoneCatalogue Create() => new([
        new WorkMilestoneDefinition(
            "work.session.actions.1000",
            WorkCounterKind.TotalActions,
            WorkMilestoneScope.CurrentSession,
            1_000,
            5 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.RepeatPerSession),
        new WorkMilestoneDefinition(
            "work.session.actions.5000",
            WorkCounterKind.TotalActions,
            WorkMilestoneScope.CurrentSession,
            5_000,
            20 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.RepeatPerSession),
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
            "work.lifetime.actions.10000",
            WorkCounterKind.TotalActions,
            WorkMilestoneScope.Lifetime,
            10_000,
            25 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.OnceLifetime),
        new WorkMilestoneDefinition(
            "work.lifetime.actions.100000",
            WorkCounterKind.TotalActions,
            WorkMilestoneScope.Lifetime,
            100_000,
            150 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.OnceLifetime),
        new WorkMilestoneDefinition(
            "work.lifetime.actions.1000000",
            WorkCounterKind.TotalActions,
            WorkMilestoneScope.Lifetime,
            1_000_000,
            1_000 * RewardLedger.MilliCreditsPerCredit,
            WorkMilestoneRepeatPolicy.OnceLifetime),
    ]);
}

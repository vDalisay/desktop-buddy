using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.App;

public partial class LifecycleCoordinator
{
    private Func<IReadOnlyList<BuddyRuntimeProgressBinding>>? _activeBuddyProgressProvider;

    /// <summary>
    /// Supplies the active Scene's ordered Buddy bindings for Buddy-local lifecycle semantics. The
    /// compatibility binding configured on this coordinator still owns the single account-wide
    /// passive-income/time write; the provider is used only for additional Buddy mood/fun/hunger.
    /// </summary>
    public void SetActiveBuddyProgressProvider(
        Func<IReadOnlyList<BuddyRuntimeProgressBinding>>? provider) =>
        _activeBuddyProgressProvider = provider;

    private void ApplyAdditionalActiveBuddyLifecycle(
        double elapsed,
        HungerActivity hungerActivity,
        bool workMode)
    {
        // Work Mode deliberately focuses one selected compatibility Buddy and suspends normal Play
        // simulation for the rest of the active Scene. Until Work gains an explicit actor selector,
        // the configured roster-head binding is that focused Buddy and secondary lifecycle freezes.
        if (workMode || _activeBuddyProgressProvider is null)
            return;

        IReadOnlyList<BuddyRuntimeProgressBinding> bindings = _activeBuddyProgressProvider();
        BuddyIdentityState? configuredBuddy = _progress.BuddyProgress;
        for (int index = 0; index < bindings.Count; index++)
        {
            BuddyRuntimeProgressBinding binding = bindings[index];
            // The configured compatibility binding was already advanced by ApplyAcceptedSpan. Skip
            // by authoritative Buddy object, not wrapper identity, because Scene binding registries
            // intentionally create lightweight coordinator wrappers on demand.
            if (configuredBuddy is not null && ReferenceEquals(binding.BuddyProgress, configuredBuddy))
                continue;
            // Legacy mode has only one aggregate and never installs a roster provider, but retain a
            // defensive identity skip in case a scenario supplies one.
            if (!binding.IsSplit && ReferenceEquals(binding.LegacyProgress, _progress.LegacyProgress))
                continue;

            binding.DriftMood(elapsed);
            binding.RechargeFun(elapsed);
            binding.DrainHunger(elapsed, hungerActivity);
        }
    }
}

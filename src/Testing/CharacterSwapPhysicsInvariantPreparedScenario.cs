using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Physics invariant at the actual activation boundary: async load/compile has completed,
/// then no engine frame elapses between the before/after samples around PhysicsTick().
/// </summary>
public sealed class CharacterSwapPhysicsInvariantPreparedScenario : IScenario
{
    public string Id => "character_swap_physics_invariant";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        (BuddyLab lab, string root, CharacterStore store) =
            await CharacterSelectionScenarioSupport.CreateLabAsync(tree, Id);
        try
        {
            Guid id = Guid.Parse("62000000-0000-4000-8000-000000000002");
            await store.SaveAsync(
                CharacterSelectionScenarioSupport.Character(id, "Blue", "#2368D8"),
                CancellationToken.None);
            var selection = new CharacterSelectionState();
            var memory = new InMemoryProgressStore();
            SaveCoordinator saves = CharacterSelectionScenarioSupport.Saves(
                selection, memory, out BuddyProgressState progress);
            var coordinator = new CharacterSelectionCoordinator(
                store, selection, lab.VisualPresenter.RigView, saves);

            CharacterActivationResult queued = await coordinator.QueueUseCharacterAsync(
                id, CancellationToken.None);
            BuddyVisualRigTrustSnapshot trusted = lab.VisualPresenter.RigView.CaptureTrustSnapshot();
            var before = new BodyInvariant[PuppetRigProfile.RequiredPartCount];
            for (int index = 0; index < before.Length; index++)
                before[index] = BodyInvariant.Capture(lab.Buddy.Rig.GetPart((BuddyPartId)index));
            ProgressSnapshot progressBefore = progress.Snapshot();
            bool beforeTickUnchanged = selection.ActiveCharacterId is null &&
                lab.VisualPresenter.RigView.ActiveAppearance is null;

            coordinator.PhysicsTick();

            bool bodiesEqual = true;
            for (int index = 0; index < before.Length; index++)
                bodiesEqual &= before[index] == BodyInvariant.Capture(lab.Buddy.Rig.GetPart((BuddyPartId)index));
            bool progressEqual = ProgressEquals(progressBefore, progress.Snapshot());
            bool trustEqual = lab.VisualPresenter.RigView.TrustedGeometryMatches(trusted);
            bool invariant = queued.WasQueued && beforeTickUnchanged && bodiesEqual &&
                trustEqual && progressEqual &&
                selection.ActiveCharacterId == id &&
                lab.VisualPresenter.RigView.ActiveAppearance?.CharacterId == id;
            checks.Add(new StartupCheck("a6_swap_visual_only_at_fixed_tick", invariant,
                $"queued={queued.Status} before_tick={beforeTickUnchanged} bodies={bodiesEqual} " +
                $"progress={progressEqual} trust={trustEqual} selection={selection.ActiveCharacterId}"));
        }
        finally
        {
            CharacterSelectionScenarioSupport.Cleanup(lab, root);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }

    private static bool ProgressEquals(
        in ProgressSnapshot left,
        in ProgressSnapshot right) =>
        left.Revision == right.Revision &&
        left.BalanceMilliCredits == right.BalanceMilliCredits &&
        string.Equals(left.SelectedToolId, right.SelectedToolId, StringComparison.Ordinal) &&
        left.UnlockedToolIds.SequenceEqual(right.UnlockedToolIds, StringComparer.Ordinal) &&
        left.Mood.Equals(right.Mood) &&
        left.HarmfulContentIds.SequenceEqual(right.HarmfulContentIds, StringComparer.Ordinal) &&
        left.Traits == right.Traits &&
        StatisticsEqual(left.Statistics, right.Statistics) &&
        left.Times == right.Times &&
        ExtensionsEqual(left.Extensions, right.Extensions) &&
        FunInterestEqual(left.FunInterest, right.FunInterest) &&
        left.Fullness.Equals(right.Fullness);

    private static bool StatisticsEqual(
        in ProgressStatistics left,
        in ProgressStatistics right) =>
        left.ScoredImpacts == right.ScoredImpacts &&
        left.Knockouts == right.Knockouts &&
        left.CareAwards == right.CareAwards &&
        left.TrustResets == right.TrustResets &&
        left.EarnedMilliCredits == right.EarnedMilliCredits &&
        left.SuccessfulCatches == right.SuccessfulCatches &&
        left.TotalPainMilli == right.TotalPainMilli &&
        left.BestOneSecondMilliCredits == right.BestOneSecondMilliCredits &&
        left.BestThreeSecondMilliCredits == right.BestThreeSecondMilliCredits &&
        left.BestTenSecondMilliCredits == right.BestTenSecondMilliCredits &&
        left.HighestMood.Equals(right.HighestMood) &&
        left.LowestMood.Equals(right.LowestMood) &&
        DictionaryEqual(left.ToolUses, right.ToolUses) &&
        DictionaryEqual(left.ToolPainMilli, right.ToolPainMilli);

    private static bool ExtensionsEqual(
        ProgressExtensionData? left,
        ProgressExtensionData? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return string.Equals(
                left.UnknownSelectedToolId,
                right.UnknownSelectedToolId,
                StringComparison.Ordinal) &&
            SequenceEqual(left.UnknownContentIds, right.UnknownContentIds) &&
            DictionaryEqual(left.Values, right.Values);
    }

    private static bool FunInterestEqual<T>(
        IReadOnlyList<T>? left,
        IReadOnlyList<T>? right)
        where T : IEquatable<T>
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.SequenceEqual(right);
    }

    private static bool SequenceEqual(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }

    private static bool DictionaryEqual<TValue>(
        IReadOnlyDictionary<string, TValue>? left,
        IReadOnlyDictionary<string, TValue>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        EqualityComparer<TValue> comparer = EqualityComparer<TValue>.Default;
        foreach ((string key, TValue value) in left)
        {
            if (!right.TryGetValue(key, out TValue? other) || !comparer.Equals(value, other))
                return false;
        }
        return true;
    }

    private readonly record struct BodyInvariant(
        Vector2 Position,
        Vector2 Velocity,
        float Rotation,
        float AngularVelocity,
        float Mass,
        float Radius,
        uint CollisionLayer,
        uint CollisionMask)
    {
        public static BodyInvariant Capture(PuppetPartBody body) => new(
            body.GlobalPosition,
            body.LinearVelocity,
            body.GlobalRotation,
            body.AngularVelocity,
            body.Mass,
            body.Radius,
            body.CollisionLayer,
            body.CollisionMask);
    }
}

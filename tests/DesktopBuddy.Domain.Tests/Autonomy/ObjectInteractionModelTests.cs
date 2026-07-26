using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Autonomy;

public sealed class ObjectInteractionModelTests
{
    private static readonly ObjectInteractionTuning Fast =
        new(CatchDistance: 40.0f, ApproachDistance: 200.0f, CatchTimeoutTicks: 10, HoldTicks: 4, InspectTicks: 4);

    private static readonly Func<string, bool> NothingHarmful = _ => false;
    private static readonly Func<string, bool> GloveHarmful =
        id => string.Equals(id, ContentIds.ToolBoxingGlove, StringComparison.Ordinal);

    private static ObjectCandidate Ball(float distance = 30.0f, int throwToken = 5, bool consumable = false) =>
        new(RuntimeId: 11, ContentId: ContentIds.LooseObject, ThrowToken: throwToken,
            Distance: distance, Direction: 1.0f, Consumable: consumable, AtRest: false);

    private static ObjectCandidate Food(float distance = 30.0f) =>
        new(RuntimeId: 12, ContentId: ContentIds.CareLabFood, ThrowToken: 0,
            Distance: distance, Direction: 1.0f, Consumable: true, AtRest: true);

    private static ObjectIntent Step(
        ObjectInteractionModel model,
        ObjectCandidate? candidate,
        MoodBand band = MoodBand.Content,
        Func<string, bool>? harmful = null,
        bool suppressed = false,
        bool conscious = true,
        bool holdConfirmed = false,
        bool consumeCompleted = false)
    {
        // A caller-owned buffer, exactly as the runtime component uses a preallocated
        // field array: the model stores no collection of its own.
        ObjectCandidate[] buffer = candidate is null
            ? Array.Empty<ObjectCandidate>()
            : new[] { candidate.Value };

        return model.Tick(
            buffer,
            band,
            harmful ?? NothingHarmful,
            suppressed,
            conscious,
            holdConfirmed,
            consumeCompleted);
    }

    [Fact]
    public void NearCandidate_CommitsStraightToCatch()
    {
        var model = new ObjectInteractionModel(Fast);

        ObjectIntent intent = Step(model, Ball(distance: 20.0f));

        Assert.Equal(ObjectCommand.Catch, intent.Command);
        Assert.Equal(ObjectPhase.Catch, model.Phase);
        Assert.Equal(11, model.TrackedRuntimeId);
        Assert.True(intent.IsCommitted);
    }

    [Fact]
    public void FarCandidate_ApproachesFirstThenCatches()
    {
        var model = new ObjectInteractionModel(Fast);

        ObjectIntent approach = Step(model, Ball(distance: 150.0f));
        Assert.Equal(ObjectCommand.Approach, approach.Command);
        Assert.Equal(1.0f, approach.ApproachDirection);

        ObjectIntent closing = Step(model, Ball(distance: 100.0f));
        Assert.Equal(ObjectCommand.Approach, closing.Command);

        ObjectIntent caught = Step(model, Ball(distance: 20.0f));
        Assert.Equal(ObjectCommand.Catch, caught.Command);
    }

    [Fact]
    public void FullLifecycle_RunsApproachCatchHoldInspectToss()
    {
        var model = new ObjectInteractionModel(Fast);

        Step(model, Ball(distance: 20.0f)); // → Catch
        ObjectIntent hold = Step(model, Ball(distance: 20.0f), holdConfirmed: true);
        Assert.Equal(ObjectPhase.Hold, model.Phase);
        Assert.True(hold.GrantsCatchCare);

        for (int tick = 0; tick < Fast.HoldTicks; tick++)
        {
            Step(model, Ball(distance: 5.0f), holdConfirmed: true);
        }

        Assert.Equal(ObjectPhase.Inspect, model.Phase);

        ObjectIntent outcome = ObjectIntent.None;
        for (int tick = 0; tick < Fast.InspectTicks + 1 && model.Phase == ObjectPhase.Inspect; tick++)
        {
            outcome = Step(model, Ball(distance: 5.0f), holdConfirmed: true);
        }

        // A content band tosses a safe non-consumable rather than putting it down.
        Assert.Equal(ObjectCommand.Toss, outcome.Command);
    }

    [Fact]
    public void SafeCatch_GrantsCareExactlyOncePerThrow()
    {
        var model = new ObjectInteractionModel(Fast);

        Step(model, Ball(throwToken: 7));
        ObjectIntent first = Step(model, Ball(throwToken: 7), holdConfirmed: true);
        Assert.True(first.GrantsCatchCare);

        // Dropped and re-caught on the same throw: no second grant (FR-008.3).
        Step(model, Ball(throwToken: 7), holdConfirmed: false); // → Drop
        Step(model, Ball(throwToken: 7)); // Drop completes → Idle, then re-commits
        Step(model, Ball(throwToken: 7));
        ObjectIntent second = Step(model, Ball(throwToken: 7), holdConfirmed: true);
        Assert.False(second.GrantsCatchCare);
    }

    [Fact]
    public void NewThrow_GrantsCareAgain()
    {
        var model = new ObjectInteractionModel(Fast);

        Step(model, Ball(throwToken: 1));
        Assert.True(Step(model, Ball(throwToken: 1), holdConfirmed: true).GrantsCatchCare);
        model.Reset();

        Step(model, Ball(throwToken: 2));
        Assert.True(Step(model, Ball(throwToken: 2), holdConfirmed: true).GrantsCatchCare);
    }

    [Fact]
    public void UnthrownObject_GrantsNoCatchCare()
    {
        // FR-008.3 pays for catching a *thrown* object, not for picking one up.
        var model = new ObjectInteractionModel(Fast);

        Step(model, Ball(throwToken: 0));
        ObjectIntent hold = Step(model, Ball(throwToken: 0), holdConfirmed: true);

        Assert.Equal(ObjectPhase.Hold, model.Phase);
        Assert.False(hold.GrantsCatchCare);
    }

    [Theory]
    [InlineData(MoodBand.Fearful)]
    [InlineData(MoodBand.Wary)]
    public void GuardedBands_NeverVoluntarilyEngage(MoodBand band)
    {
        // Owner decision 1: fearful and wary do not catch thrown objects.
        var model = new ObjectInteractionModel(Fast);

        ObjectIntent intent = Step(model, Ball(), band);

        Assert.Equal(ObjectCommand.None, intent.Command);
        Assert.Equal(ObjectPhase.Idle, model.Phase);
    }

    [Theory]
    [InlineData(MoodBand.Content)]
    [InlineData(MoodBand.Delighted)]
    public void WillingBands_Engage(MoodBand band)
    {
        var model = new ObjectInteractionModel(Fast);

        Assert.Equal(ObjectCommand.Catch, Step(model, Ball(), band).Command);
    }

    [Fact]
    public void HarmfulCandidate_IsNeverApproached()
    {
        var model = new ObjectInteractionModel(Fast);
        ObjectCandidate glove = Ball() with { ContentId = ContentIds.ToolBoxingGlove };

        ObjectIntent intent = Step(model, glove, harmful: GloveHarmful);

        Assert.Equal(ObjectCommand.None, intent.Command);
        Assert.Equal(ObjectPhase.Idle, model.Phase);
    }

    [Fact]
    public void LearnedHarmWhileHolding_DiscardsInsteadOfCompleting()
    {
        // RAGDOLL §4 priority 3: drop held hazards.
        var model = new ObjectInteractionModel(Fast);
        ObjectCandidate glove = Ball() with { ContentId = ContentIds.ToolBoxingGlove };

        Step(model, glove);
        Step(model, glove, holdConfirmed: true);
        Assert.Equal(ObjectPhase.Hold, model.Phase);

        ObjectIntent intent = Step(model, glove, harmful: GloveHarmful, holdConfirmed: true);

        Assert.Equal(ObjectCommand.Discard, intent.Command);
        Assert.Equal(ObjectPhase.Discard, model.Phase);
    }

    [Fact]
    public void LearnedHarmWhileApproaching_AbortsWithTheMemoryReason()
    {
        var model = new ObjectInteractionModel(Fast);
        ObjectCandidate glove = Ball(distance: 150.0f) with { ContentId = ContentIds.ToolBoxingGlove };

        Step(model, glove);
        Assert.Equal(ObjectPhase.Approach, model.Phase);

        ObjectIntent intent = Step(model, glove, harmful: GloveHarmful);

        Assert.Equal(ObjectPhase.Idle, model.Phase);
        Assert.Equal(ObjectAbortReason.HazardMemory, intent.Abort);
    }

    [Fact]
    public void HigherPriority_AbortsAndReleasesAHeldObject()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Ball());
        Step(model, Ball(), holdConfirmed: true);

        ObjectIntent intent = Step(model, Ball(), suppressed: true, holdConfirmed: true);

        // Suppression must not leave the buddy frozen around an object it no longer owns.
        Assert.Equal(ObjectCommand.Drop, intent.Command);
        Assert.Equal(ObjectAbortReason.HigherPriority, intent.Abort);
        Assert.Equal(ObjectPhase.Idle, model.Phase);
    }

    [Fact]
    public void Unconscious_AbortsEverything()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Ball());

        ObjectIntent intent = Step(model, Ball(), conscious: false);

        Assert.Equal(ObjectAbortReason.Unconscious, intent.Abort);
        Assert.Equal(ObjectPhase.Idle, model.Phase);
    }

    [Fact]
    public void LostCandidate_AbortsWhileApproaching()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Ball(distance: 150.0f));

        ObjectIntent intent = Step(model, candidate: null);

        Assert.Equal(ObjectAbortReason.CandidateLost, intent.Abort);
        Assert.Equal(ObjectPhase.Idle, model.Phase);
    }

    [Fact]
    public void RetreatingCandidate_AbortsOnceOutOfRange()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Ball(distance: 150.0f));

        ObjectIntent intent = Step(model, Ball(distance: Fast.ApproachDistance + 50.0f));

        Assert.Equal(ObjectAbortReason.OutOfReach, intent.Abort);
    }

    [Fact]
    public void CatchThatNeverLands_TimesOut()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Ball());

        ObjectIntent intent = ObjectIntent.None;
        for (int tick = 0; tick < Fast.CatchTimeoutTicks + 1 && model.Phase == ObjectPhase.Catch; tick++)
        {
            intent = Step(model, Ball());
        }

        Assert.Equal(ObjectAbortReason.PhaseTimeout, intent.Abort);
        Assert.Equal(ObjectPhase.Idle, model.Phase);
    }

    [Fact]
    public void LostGripWhileHolding_DropsRatherThanAborting()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Ball());
        Step(model, Ball(), holdConfirmed: true);

        ObjectIntent intent = Step(model, Ball(), holdConfirmed: false);

        Assert.Equal(ObjectCommand.Drop, intent.Command);
        Assert.Equal(ObjectAbortReason.None, intent.Abort);
    }

    [Fact]
    public void ConsumableReachesConsumeAndRequestsTheTransactionOnce()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Food());
        Step(model, Food(), holdConfirmed: true);

        ObjectIntent request = ObjectIntent.None;
        for (int tick = 0; tick < 40 && model.Phase != ObjectPhase.Consume; tick++)
        {
            request = Step(model, Food(distance: 5.0f), holdConfirmed: true);
        }

        Assert.Equal(ObjectPhase.Consume, model.Phase);
        Assert.True(request.RequestsConsume);

        // The request is a one-shot: later Consume ticks must not re-open a transaction.
        ObjectIntent chewing = Step(model, Food(distance: 5.0f), holdConfirmed: true);
        Assert.False(chewing.RequestsConsume);
        Assert.Equal(ObjectCommand.Consume, chewing.Command);
    }

    [Fact]
    public void ConsumeCompleted_ReturnsToIdle()
    {
        var model = new ObjectInteractionModel(Fast);
        DriveToConsume(model);

        Step(model, Food(distance: 5.0f), holdConfirmed: true, consumeCompleted: true);

        Assert.Equal(ObjectPhase.Idle, model.Phase);
        Assert.False(model.IsCommitted);
    }

    [Fact]
    public void ConsumeInterrupted_DropsWithoutCompleting()
    {
        // The runtime cancels the consume token on this path, so no cooldown begins
        // (FR-008.10). The model's job is only to stop claiming a successful consume.
        var model = new ObjectInteractionModel(Fast);
        DriveToConsume(model);

        ObjectIntent intent = Step(model, Food(distance: 5.0f), holdConfirmed: false);

        Assert.Equal(ObjectCommand.Drop, intent.Command);
        Assert.Equal(ObjectPhase.Drop, model.Phase);
    }

    [Fact]
    public void NonConsumableInAGuardedBand_IsPutDownNotTossed()
    {
        var model = new ObjectInteractionModel(Fast);
        // Commit while content, then let the mood fall before the outcome is chosen.
        Step(model, Ball());
        Step(model, Ball(), holdConfirmed: true);

        ObjectIntent outcome = ObjectIntent.None;
        for (int tick = 0; tick < 40 && model.Phase != ObjectPhase.Drop && model.Phase != ObjectPhase.Toss; tick++)
        {
            outcome = Step(model, Ball(distance: 5.0f), MoodBand.Wary, holdConfirmed: true);
        }

        Assert.Equal(ObjectCommand.Drop, outcome.Command);
    }

    [Fact]
    public void ClosestEligibleCandidateWins()
    {
        var model = new ObjectInteractionModel(Fast);
        ObjectCandidate[] candidates =
        {
            new(RuntimeId: 1, ContentIds.LooseObject, 1, 180.0f, 1.0f, false, true),
            new(RuntimeId: 2, ContentIds.LooseObject, 1, 60.0f, -1.0f, false, true),
            new(RuntimeId: 3, ContentIds.LooseObject, 1, 120.0f, 1.0f, false, true),
        };

        model.Tick(candidates, MoodBand.Content, NothingHarmful, false, true, false, false);

        Assert.Equal(2, model.TrackedRuntimeId);
    }

    [Fact]
    public void HarmfulCandidatesAreSkippedInFavourOfASafeFartherOne()
    {
        var model = new ObjectInteractionModel(Fast);
        ObjectCandidate[] candidates =
        {
            new(RuntimeId: 1, ContentIds.ToolBoxingGlove, 1, 30.0f, 1.0f, false, true),
            new(RuntimeId: 2, ContentIds.LooseObject, 1, 150.0f, -1.0f, false, true),
        };

        model.Tick(candidates, MoodBand.Content, GloveHarmful, false, true, false, false);

        Assert.Equal(2, model.TrackedRuntimeId);
    }

    [Fact]
    public void CandidatesBeyondApproachRangeAreIgnored()
    {
        var model = new ObjectInteractionModel(Fast);

        ObjectIntent intent = Step(model, Ball(distance: Fast.ApproachDistance + 1.0f));

        Assert.Equal(ObjectCommand.None, intent.Command);
        Assert.Equal(ObjectPhase.Idle, model.Phase);
    }

    [Fact]
    public void Reset_ClearsTrackingAndTheOncePerThrowLedger()
    {
        var model = new ObjectInteractionModel(Fast);
        Step(model, Ball(throwToken: 4));
        Step(model, Ball(throwToken: 4), holdConfirmed: true);

        model.Reset();

        Assert.Equal(ObjectPhase.Idle, model.Phase);
        Assert.Equal(0, model.TrackedRuntimeId);
        Assert.False(model.IsHolding);

        // After a reposition the previous throw is no longer the same event.
        Step(model, Ball(throwToken: 4));
        Assert.True(Step(model, Ball(throwToken: 4), holdConfirmed: true).GrantsCatchCare);
    }

    [Fact]
    public void Tick_RejectsANullMemoryPredicate() =>
        Assert.Throws<ArgumentNullException>(() =>
            new ObjectInteractionModel(Fast).Tick(
                ReadOnlySpan<ObjectCandidate>.Empty, MoodBand.Content, null!, false, true, false, false));

    private static void DriveToConsume(ObjectInteractionModel model)
    {
        Step(model, Food());
        Step(model, Food(), holdConfirmed: true);
        for (int tick = 0; tick < 40 && model.Phase != ObjectPhase.Consume; tick++)
        {
            Step(model, Food(distance: 5.0f), holdConfirmed: true);
        }

        Assert.Equal(ObjectPhase.Consume, model.Phase);
    }
}

using System;
using DesktopBuddy.Domain.Mood;

namespace DesktopBuddy.Domain.Autonomy;

/// <summary>The semantic object-interaction lifecycle (RAGDOLL §4 priority 5).</summary>
public enum ObjectPhase
{
    Idle,
    Approach,
    Catch,
    Hold,
    Inspect,
    Consume,
    Toss,
    Discard,
    Drop,
}

/// <summary>Why a committed object action ended without completing.</summary>
public enum ObjectAbortReason
{
    None,
    HigherPriority,
    CandidateLost,
    OutOfReach,
    HazardMemory,
    PhaseTimeout,
    Unconscious,
}

/// <summary>What the runtime should do about the tracked object this tick.</summary>
public enum ObjectCommand
{
    None,
    Approach,
    Catch,
    Hold,
    Inspect,
    Consume,
    Toss,
    Discard,
    Drop,
}

/// <summary>
/// One sensed loose object the buddy could act on. Runtime IDs are per-instance and
/// transient; <see cref="ContentId"/> is the stable string the harmful memory keys on.
/// </summary>
/// <param name="RuntimeId">Per-instance registry ID; never persisted.</param>
/// <param name="ThrowToken">
/// Identifies one throw. A safe catch grants <c>+1</c> mood once per token (FR-008.3), so a
/// caught-dropped-recaught object cannot farm mood. <c>0</c> means "not thrown".
/// </param>
/// <param name="Distance">Horizontal distance from the buddy.</param>
/// <param name="Direction">Signed direction toward the object (-1 or +1).</param>
/// <param name="Consumable">Whether a successful hold can proceed to Consume.</param>
/// <param name="AtRest">Idle objects are approachable; airborne ones are catchable.</param>
public readonly record struct ObjectCandidate(
    int RuntimeId,
    string ContentId,
    int ThrowToken,
    float Distance,
    float Direction,
    bool Consumable,
    bool AtRest)
{
    public bool IsValid => RuntimeId != 0;
}

/// <summary>Tuning for the object lifecycle. All durations are routed ticks.</summary>
/// <param name="CatchDistance">Within this distance a catch may be attempted.</param>
/// <param name="ApproachDistance">Beyond this distance the buddy must close first.</param>
/// <param name="CatchTimeoutTicks">A catch attempt that never lands aborts after this.</param>
/// <param name="HoldTicks">How long a held object is kept before inspecting.</param>
/// <param name="InspectTicks">Inspection duration before the outcome is chosen.</param>
public readonly record struct ObjectInteractionTuning(
    float CatchDistance,
    float ApproachDistance,
    int CatchTimeoutTicks,
    int HoldTicks,
    int InspectTicks)
{
    public static ObjectInteractionTuning Default => new(46.0f, 220.0f, 90, 120, 150);
}

/// <summary>The model's resolved intent for one routed tick.</summary>
public readonly record struct ObjectIntent(
    ObjectCommand Command,
    ObjectPhase Phase,
    int RuntimeId,
    float ApproachDirection,
    /// <summary>Set on the tick a safe catch first completes, once per throw token.</summary>
    bool GrantsCatchCare,
    /// <summary>Set on the tick the model asks for a consume transaction to open.</summary>
    bool RequestsConsume,
    ObjectAbortReason Abort)
{
    public static ObjectIntent None => new(
        ObjectCommand.None, ObjectPhase.Idle, 0, 0.0f, false, false, ObjectAbortReason.None);

    /// <summary>True while the model wants arbiter priority 5.</summary>
    public bool IsCommitted => Phase != ObjectPhase.Idle;
}

/// <summary>
/// The pure catch → hold → inspect → outcome state machine (RAGDOLL §4 priority 5, §4.1
/// object memory; FR-005.6, FR-008.3).
///
/// <para><b>Memory gating.</b> A candidate whose content ID is in harmful history is never
/// approached or caught voluntarily; if it is already held when the memory applies, the
/// machine goes straight to Discard — the §4 "drop held hazards" rule. The fearful band
/// refuses voluntary catches entirely, so a scared buddy does not reach for thrown objects
/// even when they are safe. Wary through delighted all catch (owner correction 2026-07-26:
/// declining from wary through neutral made a default-mood buddy ignore thrown objects).</para>
///
/// <para><b>Only a real throw is an invitation.</b> A voluntary catch target must be
/// airborne <i>and</i> carry a player throw token. A ball at rest — or one the buddy just
/// kicked with its own foot — is scenery the priority 7 obstacle hop may step over.
/// Consumables are exempt, because a meal on the floor is still worth picking up.</para>
///
/// <para><b>Catch care is once per throw.</b> <see cref="ObjectCandidate.ThrowToken"/> is
/// remembered on the tick the catch completes, so re-catching the same object after a drop
/// grants nothing until it is thrown again.</para>
///
/// <para><b>Consume is a request, not an effect.</b> The model asks
/// (<see cref="ObjectIntent.RequestsConsume"/>); <see cref="CareConsumableModel"/> owns the
/// cooldown and the mood grant, and the runtime only converts the fifth authoritative bite
/// into success. That split is what keeps a cancelled Eat from starting a cooldown
/// (FR-008.10).</para>
///
/// <para>Allocation-free: candidate scoring reads a caller-owned span, and no collection is
/// built per tick (ARCHITECTURE §23).</para>
/// </summary>
public sealed class ObjectInteractionModel
{
    private readonly ObjectInteractionTuning _tuning;
    private readonly SocialTuningSet _socialTuning;

    private ObjectPhase _phase = ObjectPhase.Idle;
    private int _runtimeId;
    private string _contentId = string.Empty;
    private int _throwToken;
    private int _phaseTicks;
    private bool _consumable;
    private int _lastRewardedThrowToken;

    public ObjectInteractionModel(
        ObjectInteractionTuning? tuning = null,
        SocialTuningSet? socialTuning = null)
    {
        _tuning = tuning ?? ObjectInteractionTuning.Default;
        _socialTuning = socialTuning ?? SocialTuningSet.Default;
        _socialTuning.Validate();
    }

    public ObjectPhase Phase => _phase;
    public int TrackedRuntimeId => _runtimeId;
    public string TrackedContentId => _contentId;
    public int PhaseTicks => _phaseTicks;
    public ObjectAbortReason LastAbort { get; private set; }

    /// <summary>True while the machine wants arbiter priority 5 this tick.</summary>
    public bool IsCommitted => _phase != ObjectPhase.Idle;

    /// <summary>True while an object is physically held by the buddy.</summary>
    public bool IsHolding => _phase is ObjectPhase.Hold or ObjectPhase.Inspect or ObjectPhase.Consume;

    /// <summary>
    /// Advances the machine one routed tick.
    /// </summary>
    /// <param name="candidates">
    /// Sensed candidates, owned by the caller. Scored fresh each tick; the machine keeps only
    /// the tracked runtime ID, never a reference.
    /// </param>
    /// <param name="moodBand">Current band; gates voluntary catches per owner decision 1.</param>
    /// <param name="isHarmful">Harmful-history predicate (delegate, not a captured closure).</param>
    /// <param name="suppressed">
    /// True when a higher arbiter priority owns actuation. Aborts any committed action and
    /// releases a held object rather than freezing mid-reach.
    /// </param>
    /// <param name="conscious">False cancels everything (priority 1).</param>
    /// <param name="holdConfirmed">
    /// Runtime feedback: the hands actually closed on the tracked object this tick.
    /// </param>
    /// <param name="consumeCompleted">
    /// Runtime feedback: the authoritative final bite landed and care was applied.
    /// </param>
    public ObjectIntent Tick(
        ReadOnlySpan<ObjectCandidate> candidates,
        MoodBand moodBand,
        Func<string, bool> isHarmful,
        bool suppressed,
        bool conscious,
        bool holdConfirmed,
        bool consumeCompleted)
    {
        ArgumentNullException.ThrowIfNull(isHarmful);

        if (!conscious)
        {
            return AbortTo(ObjectAbortReason.Unconscious);
        }

        if (suppressed)
        {
            return AbortTo(ObjectAbortReason.HigherPriority);
        }

        if (_phase == ObjectPhase.Idle)
        {
            return TryCommit(candidates, moodBand, isHarmful);
        }

        ObjectCandidate tracked = Find(candidates, _runtimeId);

        // A held object stops being sensed by the candidate scanner; holding phases trust
        // the runtime's hold confirmation instead of candidate presence.
        if (!tracked.IsValid && !IsHolding)
        {
            return AbortTo(ObjectAbortReason.CandidateLost);
        }

        if (isHarmful(_contentId))
        {
            // Learned harm while engaged: drop it and flee rather than finish the action.
            return IsHolding
                ? Transition(ObjectPhase.Discard, ObjectCommand.Discard)
                : AbortTo(ObjectAbortReason.HazardMemory);
        }

        _phaseTicks++;

        switch (_phase)
        {
            case ObjectPhase.Approach:
                if (tracked.Distance <= _tuning.CatchDistance)
                {
                    return Transition(ObjectPhase.Catch, ObjectCommand.Catch, tracked.Direction);
                }

                if (tracked.Distance > _tuning.ApproachDistance)
                {
                    return AbortTo(ObjectAbortReason.OutOfReach);
                }

                return Emit(ObjectCommand.Approach, tracked.Direction);

            case ObjectPhase.Catch:
                if (holdConfirmed)
                {
                    bool grants = GrantsCatchCare();
                    ObjectIntent intent = Transition(ObjectPhase.Hold, ObjectCommand.Hold);
                    return intent with { GrantsCatchCare = grants };
                }

                if (_phaseTicks >= _tuning.CatchTimeoutTicks)
                {
                    return AbortTo(ObjectAbortReason.PhaseTimeout);
                }

                return Emit(ObjectCommand.Catch, tracked.IsValid ? tracked.Direction : 0.0f);

            case ObjectPhase.Hold:
                if (!holdConfirmed)
                {
                    // Physically lost the grip: this is a Drop, not an abort — nothing was
                    // completed, so no consume cooldown may start (FR-008.10).
                    return Transition(ObjectPhase.Drop, ObjectCommand.Drop);
                }

                return _phaseTicks >= _tuning.HoldTicks
                    ? Transition(ObjectPhase.Inspect, ObjectCommand.Inspect)
                    : Emit(ObjectCommand.Hold);

            case ObjectPhase.Inspect:
                if (!holdConfirmed)
                {
                    return Transition(ObjectPhase.Drop, ObjectCommand.Drop);
                }

                if (_phaseTicks < _tuning.InspectTicks)
                {
                    return Emit(ObjectCommand.Inspect);
                }

                if (_consumable)
                {
                    ObjectIntent consume = Transition(ObjectPhase.Consume, ObjectCommand.Consume);
                    return consume with { RequestsConsume = true };
                }

                // Non-consumable: a content band tosses it playfully, a guarded band puts it
                // down. Toss direction policy (away from the cursor) is a runtime concern.
                return moodBand is MoodBand.Content or MoodBand.Delighted
                    ? Transition(ObjectPhase.Toss, ObjectCommand.Toss)
                    : Transition(ObjectPhase.Drop, ObjectCommand.Drop);

            case ObjectPhase.Consume:
                if (consumeCompleted)
                {
                    return Complete();
                }

                if (!holdConfirmed)
                {
                    // Interrupted mid-meal: drop, and the runtime cancels the consume token
                    // so no cooldown begins.
                    return Transition(ObjectPhase.Drop, ObjectCommand.Drop);
                }

                return Emit(ObjectCommand.Consume);

            case ObjectPhase.Toss:
            case ObjectPhase.Discard:
            case ObjectPhase.Drop:
                // One-shot releases: the runtime applies the impulse on the tick it sees the
                // command, then the machine returns to Idle.
                return Complete();

            default:
                return Complete();
        }
    }

    /// <summary>
    /// Clears all lifecycle state. Used by hard reposition and session resume. The
    /// once-per-throw reward ledger is cleared too: after a reposition the previous throw is
    /// no longer the same event.
    /// </summary>
    public void Reset()
    {
        _phase = ObjectPhase.Idle;
        _runtimeId = 0;
        _contentId = string.Empty;
        _throwToken = 0;
        _phaseTicks = 0;
        _consumable = false;
        _lastRewardedThrowToken = 0;
        LastAbort = ObjectAbortReason.None;
    }

    private ObjectIntent TryCommit(
        ReadOnlySpan<ObjectCandidate> candidates,
        MoodBand moodBand,
        Func<string, bool> isHarmful)
    {
        SocialBandTuning band = _socialTuning.For(moodBand);
        if (!band.WillCatch)
        {
            // Fearful and wary never voluntarily engage an object (owner decision 1).
            return ObjectIntent.None;
        }

        int bestIndex = -1;
        float bestDistance = float.MaxValue;
        bool bestIsAirborne = false;

        for (int index = 0; index < candidates.Length; index++)
        {
            ObjectCandidate candidate = candidates[index];
            if (!candidate.IsValid ||
                candidate.Distance > _tuning.ApproachDistance ||
                isHarmful(candidate.ContentId))
            {
                continue;
            }

            // Only a live player throw is an invitation. A ball lying on the floor is
            // scenery, and — critically — so is a ball the buddy just kicked with its own
            // foot: "moving" is not the same as "thrown", and the throw token is what
            // distinguishes them. Without this, any resting object in the walking path is
            // claimed by priority 5 the moment it is nudged, so the priority 7 obstacle
            // hop can never fire. Food is exempt: a meal on the floor is worth picking up.
            if (!candidate.Consumable && (candidate.AtRest || candidate.ThrowToken == 0))
            {
                continue;
            }

            // Safety and memory are filters above; among what survives, an airborne
            // object outranks any resting one. A thrown object is a moment the buddy
            // can miss, so a nearer idle prop must never steal the catch (FR-008.3).
            bool airborne = !candidate.AtRest;
            if (bestIndex >= 0 && bestIsAirborne && !airborne)
            {
                continue;
            }

            bool betterClass = airborne && !bestIsAirborne;
            if (bestIndex < 0 || betterClass || candidate.Distance < bestDistance)
            {
                bestDistance = candidate.Distance;
                bestIsAirborne = airborne;
                bestIndex = index;
            }
        }

        if (bestIndex < 0)
        {
            return ObjectIntent.None;
        }

        ObjectCandidate chosen = candidates[bestIndex];
        _runtimeId = chosen.RuntimeId;
        _contentId = chosen.ContentId;
        _throwToken = chosen.ThrowToken;
        _consumable = chosen.Consumable;
        _phaseTicks = 0;
        LastAbort = ObjectAbortReason.None;

        return chosen.Distance <= _tuning.CatchDistance
            ? Transition(ObjectPhase.Catch, ObjectCommand.Catch, chosen.Direction)
            : Transition(ObjectPhase.Approach, ObjectCommand.Approach, chosen.Direction);
    }

    private bool GrantsCatchCare()
    {
        // Only a real throw grants care, and only once for that throw (FR-008.3).
        if (_throwToken == 0 || _throwToken == _lastRewardedThrowToken)
        {
            return false;
        }

        _lastRewardedThrowToken = _throwToken;
        return true;
    }

    private static ObjectCandidate Find(ReadOnlySpan<ObjectCandidate> candidates, int runtimeId)
    {
        for (int index = 0; index < candidates.Length; index++)
        {
            if (candidates[index].RuntimeId == runtimeId)
            {
                return candidates[index];
            }
        }

        return default;
    }

    private ObjectIntent Transition(ObjectPhase phase, ObjectCommand command, float direction = 0.0f)
    {
        _phase = phase;
        _phaseTicks = 0;
        // A transition is not an abort. Reporting the previous abort reason here made
        // the runtime cancel a live consume token on an ordinary phase change.
        return new ObjectIntent(
            command,
            phase,
            _runtimeId,
            direction,
            GrantsCatchCare: false,
            RequestsConsume: false,
            ObjectAbortReason.None);
    }

    private ObjectIntent Emit(ObjectCommand command, float direction = 0.0f) =>
        new(command, _phase, _runtimeId, direction, false, false, ObjectAbortReason.None);

    private ObjectIntent AbortTo(ObjectAbortReason reason)
    {
        if (_phase == ObjectPhase.Idle)
        {
            LastAbort = ObjectAbortReason.None;
            return ObjectIntent.None;
        }

        bool wasHolding = IsHolding;
        int runtimeId = _runtimeId;
        Reset();
        LastAbort = reason;

        // Aborting while holding must release: a suppressed layer may not leave the buddy
        // frozen around an object it is no longer deciding about.
        return new ObjectIntent(
            wasHolding ? ObjectCommand.Drop : ObjectCommand.None,
            ObjectPhase.Idle,
            runtimeId,
            0.0f,
            false,
            false,
            reason);
    }

    private ObjectIntent Complete()
    {
        int runtimeId = _runtimeId;
        int rewarded = _lastRewardedThrowToken;
        Reset();
        _lastRewardedThrowToken = rewarded;
        return new ObjectIntent(
            ObjectCommand.None,
            ObjectPhase.Idle,
            runtimeId,
            0.0f,
            false,
            false,
            ObjectAbortReason.None);
    }
}

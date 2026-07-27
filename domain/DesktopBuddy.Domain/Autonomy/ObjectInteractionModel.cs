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
/// <param name="AtRest">
/// A resting object is scooped off the ground; an airborne one is caught out of the air.
/// The two use different commit gates because the ground is further from the shoulders than
/// an arm is long.
/// </param>
/// <param name="Ignored">
/// Set while the runtime wants this object left alone — most importantly for a short window
/// after the buddy itself put it down, so it walks over its own discard instead of picking
/// the same object up forever.
/// </param>
/// <param name="GroundDistance">
/// Horizontal distance only. A ground pickup is about standing <i>over</i> the object, not
/// about how far it is from the shoulders: the floor is roughly `66 px` below the shoulder
/// line, so a straight-line gate is only satisfiable once the buddy's feet are already
/// kicking the object away.
/// </param>
public readonly record struct ObjectCandidate(
    int RuntimeId,
    string ContentId,
    int ThrowToken,
    float Distance,
    float Direction,
    bool Consumable,
    bool AtRest,
    bool Ignored = false,
    float GroundDistance = 0.0f,
    /// <summary>
    /// The player is currently carrying this object. The buddy may commit to it — that is what
    /// makes it watch the ball and get its hands up before the throw — but the catch can never
    /// confirm while the player still holds it.
    /// </summary>
    bool PlayerHeld = false)
{
    public bool IsValid => RuntimeId != 0;

    /// <summary>The distance this candidate's commit gate is measured against.</summary>
    public float EngageDistance => AtRest && !PlayerHeld ? GroundDistance : Distance;
}

/// <summary>Tuning for the object lifecycle. All durations are routed ticks.</summary>
/// <param name="CatchDistance">
/// Within this distance of the shoulders an airborne catch may be attempted. It is a
/// <i>decision</i> gate, not an arm length — the runtime clamps how far the hands actually
/// extend, so a generous value only means the buddy puts its hands up sooner.
/// </param>
/// <param name="ScoopDistance">
/// Horizontal distance within which a resting object may be scooped, measured against
/// <see cref="ObjectCandidate.GroundDistance"/>. It must leave room to stop before the feet
/// reach the object, or the buddy kicks away the thing it is trying to pick up.
/// </param>
/// <param name="ApproachDistance">Beyond this distance the buddy must close first.</param>
/// <param name="CatchTimeoutTicks">A catch attempt that never lands aborts after this.</param>
/// <param name="HoldTicks">How long a held object is kept before inspecting.</param>
/// <param name="InspectTicks">Inspection duration before the outcome is chosen.</param>
/// <param name="TossTicks">
/// How long the toss gesture holds priority 5. A return throw is a two-beat motion — draw
/// back, then release — so the phase must outlive a single tick for the runtime to play it.
/// </param>
public readonly record struct ObjectInteractionTuning(
    float CatchDistance,
    float ApproachDistance,
    int CatchTimeoutTicks,
    int HoldTicks,
    int InspectTicks,
    int TossTicks = 20,
    float ScoopDistance = 26.0f)
{
    public static ObjectInteractionTuning Default =>
        new(72.0f, 220.0f, 90, 120, 150, 20, 26.0f);

    /// <summary>The commit gate for one candidate, by how it must be picked up.</summary>
    public float EngageDistanceFor(bool atRest) => atRest ? ScoopDistance : CatchDistance;
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
/// <para><b>Rest state picks the flavour, not the eligibility.</b> A thrown object is caught
/// out of the air against <see cref="ObjectInteractionTuning.CatchDistance"/>; a resting one is
/// scooped off the floor against the horizontal
/// <see cref="ObjectInteractionTuning.ScoopDistance"/>. Both are engaged. What keeps the
/// priority 7 obstacle hop reachable is <see cref="ObjectCandidate.Ignored"/> — a cooling-off
/// window after the buddy itself put something down — not a blanket refusal to pick things
/// up, which simply removed ground pickup altogether.</para>
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
    private bool _trackedAtRest;
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

    /// <summary>
    /// True when the tracked object was resting when engaged, so the runtime should play a
    /// ground scoop rather than an in-air catch.
    /// </summary>
    public bool TrackedAtRest => _trackedAtRest;
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

        // Only the phases that are still chasing something need it to be visible. Once the
        // object is in hand — or on its way out of it — the candidate scanner deliberately
        // stops reporting it, so requiring presence there aborts a release mid-gesture.
        if (!tracked.IsValid && _phase is ObjectPhase.Approach or ObjectPhase.Catch)
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
                _trackedAtRest = tracked.AtRest;
                if (tracked.EngageDistance <= _tuning.EngageDistanceFor(tracked.AtRest))
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

                // Waiting on the player is not a failed catch. While they carry the ball the
                // buddy holds its ready pose indefinitely, so that when the throw finally
                // comes its hands are already up instead of starting to react.
                if (_phaseTicks >= _tuning.CatchTimeoutTicks && !tracked.PlayerHeld)
                {
                    return AbortTo(ObjectAbortReason.PhaseTimeout);
                }

                if (tracked.PlayerHeld)
                {
                    _phaseTicks = 0;
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
                // The throw keeps priority 5 for its whole gesture so the runtime can draw
                // the hand back and then release; it is not a one-tick impulse.
                return _phaseTicks >= _tuning.TossTicks
                    ? Complete()
                    : Emit(ObjectCommand.Toss);

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
        _trackedAtRest = false;
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


            // Objects the runtime is deliberately leaving alone — chiefly one the buddy
            // itself just put down. That window is what lets the priority 7 obstacle hop
            // ever fire: without it, priority 5 reclaims the same ball forever and the
            // buddy can never step over anything.
            if (candidate.Ignored)
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

        _trackedAtRest = chosen.AtRest;
        return chosen.EngageDistance <= _tuning.EngageDistanceFor(chosen.AtRest)
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

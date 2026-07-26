using System;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;

namespace DesktopBuddy.Domain.Autonomy;

/// <summary>
/// The RAGDOLL §4 arbitration ladder, lowest number highest priority. Priority 0 is a
/// safety exception, not a behavior; priorities 1–7 may never hard-set a transform.
/// </summary>
public enum BehaviorPriority
{
    /// <summary>Invalid/out-of-bounds fail-safe: the only immediate hard reposition path.</summary>
    Failsafe = 0,

    /// <summary>Unconscious: all active drive and object decisions disabled.</summary>
    Unconscious = 1,

    /// <summary>Assisted self-righting outranks voluntary goals.</summary>
    SelfRighting = 2,

    /// <summary>Burning or a recognized nearby hazard: drop held hazards and flee.</summary>
    Hazard = 3,

    /// <summary>Conscious and afraid while grabbed: resistance intent opposing the tether.</summary>
    GrabResistance = 4,

    /// <summary>Committed catch/hold/inspect/consume/toss/discard action.</summary>
    ObjectAction = 5,

    /// <summary>Emotional/social goal from mood band, transient emotion, and tool memory.</summary>
    Social = 6,

    /// <summary>Ambient autonomy: idle, walk, obstacle hop, non-urgent interactions.</summary>
    Ambient = 7,
}

/// <summary>What the social layer wants this tick.</summary>
public enum SocialStance
{
    None,
    KeepDistance,
    Flee,
    Approach,
    Greet,
}

/// <summary>
/// Per-band social vocabulary (owner decision 1, 2026-07-24). Distances and cadences are
/// delegated engineering tuning judged at the M4 owner gate.
/// </summary>
/// <param name="StandoffDistance">Below this the buddy actively increases distance.</param>
/// <param name="ApproachDistance">Above this an approaching band closes in.</param>
/// <param name="Hysteresis">
/// Dead band applied to both envelopes so a buddy hovering at a threshold cannot
/// flip-flop at 120 Hz (ARCHITECTURE §23).
/// </param>
/// <param name="WillApproach">Whether this band voluntarily closes distance at all.</param>
/// <param name="WillCatch">Whether this band accepts a voluntary catch (FR-008.3 gate).</param>
/// <param name="LocomotionScale">Bounded locomotion scale for social movement.</param>
/// <param name="GreetIntervalTicks">Minimum routed ticks between waves/glances; <c>0</c> disables.</param>
public readonly record struct SocialBandTuning(
    float StandoffDistance,
    float ApproachDistance,
    float Hysteresis,
    bool WillApproach,
    bool WillCatch,
    float LocomotionScale,
    int GreetIntervalTicks)
{
    /// <summary>Fearful: maximum distance, flees approach, never catches.</summary>
    public static SocialBandTuning Fearful => new(260.0f, 0.0f, 24.0f, false, false, 1.0f, 0);

    /// <summary>Wary: moderate standoff, never approaches, no catches.</summary>
    public static SocialBandTuning Wary => new(150.0f, 0.0f, 18.0f, false, false, 0.8f, 0);

    /// <summary>Neutral: current ambient baseline — the social layer stands down.</summary>
    public static SocialBandTuning Neutral => new(0.0f, 0.0f, 12.0f, false, false, 0.0f, 0);

    /// <summary>Content: occasional approach, willing catches, occasional wave.</summary>
    public static SocialBandTuning Content => new(0.0f, 170.0f, 14.0f, true, true, 0.7f, 900);

    /// <summary>Delighted: eager approach and catch, frequent wave/glance.</summary>
    public static SocialBandTuning Delighted => new(0.0f, 110.0f, 14.0f, true, true, 1.0f, 360);

    public static SocialBandTuning For(MoodBand band) => band switch
    {
        MoodBand.Fearful => Fearful,
        MoodBand.Wary => Wary,
        MoodBand.Neutral => Neutral,
        MoodBand.Content => Content,
        MoodBand.Delighted => Delighted,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown mood band."),
    };
}

/// <summary>Arbiter-wide tuning.</summary>
/// <param name="CommitTicks">
/// How long a chosen layer keeps actuation once selected. Equal-or-lower priority cannot
/// take over inside the window; a higher priority preempts immediately.
/// </param>
/// <param name="HopPropensityThreshold">
/// Obstacle hops require <c>traits.ObstacleHopPropensity &gt;= threshold</c> in addition to
/// obstacle evidence, committed path, and stable support. Pure-timer ambient jumps stay OFF
/// (DECISIONS 2026-07-20).
/// </param>
public readonly record struct BehaviorArbiterTuning(int CommitTicks, int HopPropensityThreshold)
{
    public static BehaviorArbiterTuning Default => new(36, 35);
}

/// <summary>An immutable read of everything the ladder needs, built fresh each routed tick.</summary>
public readonly record struct BehaviorSnapshot(
    int Tick,
    Consciousness Consciousness,
    bool RequiresFailsafeReposition,
    bool SelfRightingEligible,
    bool HazardPresent,
    /// <summary>Signed direction that increases distance from the hazard (-1 or +1).</summary>
    float HazardFleeDirection,
    bool Grabbed,
    bool AfraidOfGrab,
    /// <summary>Signed direction that increases distance from the grab anchor (-1 or +1).</summary>
    float GrabFleeDirection,
    bool HasStableSupport,
    bool WallBlockedLeft,
    bool WallBlockedRight,
    MoodBand MoodBand,
    /// <summary>A committed object action exists and still wants actuation this tick.</summary>
    bool ObjectActionCommitted,
    float ObjectApproachDirection,
    bool SocialTargetValid,
    /// <summary>Signed direction from the buddy toward the social target (-1 or +1).</summary>
    float SocialTargetDirection,
    float SocialTargetDistance,
    bool AmbientDriveActive,
    float AmbientWalkDirection,
    float AmbientLocomotionScale,
    bool ObstacleInCommittedPath);

/// <summary>One resolved actuation decision.</summary>
public readonly record struct ActuationIntent(
    BehaviorPriority Owner,
    bool DriveActive,
    float WalkDirection,
    float LocomotionScale,
    bool JumpRequested,
    bool GuardRequested,
    bool ResistGrab,
    SocialStance Stance,
    bool GreetRequested)
{
    /// <summary>No layer wants actuation (or every layer is suppressed).</summary>
    public static ActuationIntent Idle(BehaviorPriority owner) =>
        new(owner, false, 0.0f, 0.0f, false, false, false, SocialStance.None, false);
}

/// <summary>
/// Diagnostics for scenarios and the lab panel. Semantic, not presentation: a scenario can
/// assert which layer owned the tick and why a lower one was suppressed.
/// </summary>
public readonly record struct ArbiterDiagnostics(
    BehaviorPriority Owner,
    BehaviorPriority HighestEligible,
    int CommitTicksRemaining,
    bool PreemptedThisTick,
    bool AmbientSuppressed);

/// <summary>
/// The pure §4 priority ladder. One <see cref="Resolve"/> call per routed tick turns an
/// immutable <see cref="BehaviorSnapshot"/> into exactly one <see cref="ActuationIntent"/>.
///
/// <para><b>Commitment.</b> Once a layer wins, it keeps actuation for
/// <see cref="BehaviorArbiterTuning.CommitTicks"/> so goals cannot flip-flop at 120 Hz. The
/// window binds only equal or lower priorities: a higher priority preempts on the tick it
/// becomes eligible, and the commitment is invalidated the moment the owning layer stops
/// being eligible (a caught object dropped, a hazard gone, a grab released).</para>
///
/// <para><b>Suppression is not erasure.</b> A higher layer owning actuation suppresses
/// lower-layer <i>drive</i>; it never clears mood, memory, statistics, or externally applied
/// physics. <see cref="ArbiterDiagnostics.AmbientSuppressed"/> is what the runtime uses to
/// pause ambient decision/RNG progression, so a suppressed autonomy stream resumes where it
/// left off instead of silently advancing.</para>
///
/// <para>Allocation-free: state is a handful of fields, every payload is a
/// <c>readonly record struct</c>, and nothing here allocates or captures.</para>
/// </summary>
public sealed class BehaviorArbiterModel
{
    private readonly BehaviorArbiterTuning _tuning;

    private BehaviorPriority _owner = BehaviorPriority.Ambient;
    private int _commitTicksRemaining;
    private int _lastGreetTick = int.MinValue;

    public BehaviorArbiterModel(BehaviorArbiterTuning? tuning = null)
    {
        _tuning = tuning ?? BehaviorArbiterTuning.Default;
        if (_tuning.CommitTicks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tuning), "CommitTicks must be >= 0.");
        }
    }

    public ArbiterDiagnostics Diagnostics { get; private set; }

    /// <summary>The layer that owned actuation on the last resolved tick.</summary>
    public BehaviorPriority Owner => _owner;

    /// <summary>
    /// Resolves the ladder for one routed tick. <paramref name="traits"/> gates obstacle
    /// hops; it is per-save and never resampled here.
    /// </summary>
    public ActuationIntent Resolve(in BehaviorSnapshot snapshot, in BuddyTraits traits)
    {
        BehaviorPriority eligible = HighestEligible(snapshot);
        bool committedStillEligible =
            _commitTicksRemaining > 0 && IsEligible(snapshot, _owner);

        BehaviorPriority owner;
        bool preempted = false;

        if (committedStillEligible && eligible >= _owner)
        {
            // Inside the window, an equal-or-lower priority cannot take over.
            owner = _owner;
        }
        else
        {
            owner = eligible;
            preempted = owner != _owner;
        }

        if (owner != _owner || _commitTicksRemaining <= 0)
        {
            _owner = owner;
            _commitTicksRemaining = _tuning.CommitTicks;
        }
        else
        {
            _commitTicksRemaining--;
        }

        ActuationIntent intent = Actuate(snapshot, traits, owner);

        Diagnostics = new ArbiterDiagnostics(
            owner,
            eligible,
            _commitTicksRemaining,
            preempted,
            owner != BehaviorPriority.Ambient);

        return intent;
    }

    /// <summary>
    /// Drops commitment state. Used by hard reposition and session resume, which clear all
    /// transient behavior state without touching persistent mood/memory.
    /// </summary>
    public void Reset()
    {
        _owner = BehaviorPriority.Ambient;
        _commitTicksRemaining = 0;
        _lastGreetTick = int.MinValue;
        Diagnostics = default;
    }

    private static bool IsEligible(in BehaviorSnapshot snapshot, BehaviorPriority priority) =>
        priority switch
        {
            BehaviorPriority.Failsafe => snapshot.RequiresFailsafeReposition,
            BehaviorPriority.Unconscious => snapshot.Consciousness == Consciousness.Unconscious,
            BehaviorPriority.SelfRighting => snapshot.SelfRightingEligible,
            BehaviorPriority.Hazard => snapshot.HazardPresent,
            // The tether stays physically active regardless of intent; only a conscious,
            // afraid, supported buddy generates resistance (RAGDOLL §4 priority 4).
            BehaviorPriority.GrabResistance =>
                snapshot.Grabbed && snapshot.AfraidOfGrab &&
                snapshot.Consciousness == Consciousness.Conscious,
            BehaviorPriority.ObjectAction => snapshot.ObjectActionCommitted,
            BehaviorPriority.Social => SocialStanceFor(snapshot) != SocialStance.None,
            BehaviorPriority.Ambient => snapshot.AmbientDriveActive,
            _ => false,
        };

    private static BehaviorPriority HighestEligible(in BehaviorSnapshot snapshot)
    {
        // Ordered walk, highest priority first. Ambient is the floor and is returned even
        // when inactive so the owner is always defined.
        for (BehaviorPriority priority = BehaviorPriority.Failsafe;
             priority < BehaviorPriority.Ambient;
             priority++)
        {
            if (IsEligible(snapshot, priority))
            {
                return priority;
            }
        }

        return BehaviorPriority.Ambient;
    }

    private static SocialStance SocialStanceFor(in BehaviorSnapshot snapshot)
    {
        if (!snapshot.SocialTargetValid)
        {
            return SocialStance.None;
        }

        SocialBandTuning band = SocialBandTuning.For(snapshot.MoodBand);
        float distance = snapshot.SocialTargetDistance;

        if (band.StandoffDistance > 0.0f)
        {
            // Hysteresis: retreat starts inside the standoff distance and only stops once
            // the buddy has cleared it by the dead band.
            if (distance < band.StandoffDistance)
            {
                return snapshot.MoodBand == MoodBand.Fearful
                    ? SocialStance.Flee
                    : SocialStance.KeepDistance;
            }

            return distance < band.StandoffDistance + band.Hysteresis
                ? SocialStance.KeepDistance
                : SocialStance.None;
        }

        if (band.WillApproach && distance > band.ApproachDistance + band.Hysteresis)
        {
            return SocialStance.Approach;
        }

        if (band.GreetIntervalTicks > 0 && distance <= band.ApproachDistance)
        {
            return SocialStance.Greet;
        }

        return SocialStance.None;
    }

    private ActuationIntent Actuate(
        in BehaviorSnapshot snapshot,
        in BuddyTraits traits,
        BehaviorPriority owner)
    {
        switch (owner)
        {
            case BehaviorPriority.Failsafe:
            case BehaviorPriority.Unconscious:
            case BehaviorPriority.SelfRighting:
                // The recovery/standing workers own these outcomes; the arbiter's job is
                // only to guarantee no voluntary drive competes with them.
                return ActuationIntent.Idle(owner);

            case BehaviorPriority.Hazard:
                return new ActuationIntent(
                    owner,
                    DriveActive: true,
                    WalkDirection: BlockedDirection(snapshot, snapshot.HazardFleeDirection),
                    LocomotionScale: 1.0f,
                    JumpRequested: false,
                    GuardRequested: true,
                    ResistGrab: false,
                    Stance: SocialStance.Flee,
                    GreetRequested: false);

            case BehaviorPriority.GrabResistance:
                // Resistance is a *walk* away from the tether plus strain force, not a lump
                // shove: the gait must run so the feet visibly fight (owner feel note
                // 2026-07-25). The tether stays physically active regardless (RAGDOLL §4).
                return new ActuationIntent(
                    owner,
                    DriveActive: true,
                    WalkDirection: BlockedDirection(snapshot, snapshot.GrabFleeDirection),
                    LocomotionScale: 1.0f,
                    JumpRequested: false,
                    GuardRequested: false,
                    ResistGrab: true,
                    Stance: SocialStance.Flee,
                    GreetRequested: false);

            case BehaviorPriority.ObjectAction:
                return new ActuationIntent(
                    owner,
                    DriveActive: snapshot.ObjectApproachDirection != 0.0f,
                    WalkDirection: BlockedDirection(snapshot, snapshot.ObjectApproachDirection),
                    LocomotionScale: 0.85f,
                    JumpRequested: false,
                    GuardRequested: false,
                    ResistGrab: false,
                    Stance: SocialStance.None,
                    GreetRequested: false);

            case BehaviorPriority.Social:
                return SocialIntent(snapshot);

            default:
                return AmbientIntent(snapshot, traits);
        }
    }

    private ActuationIntent SocialIntent(in BehaviorSnapshot snapshot)
    {
        SocialStance stance = SocialStanceFor(snapshot);
        SocialBandTuning band = SocialBandTuning.For(snapshot.MoodBand);

        float direction = stance switch
        {
            SocialStance.Approach => snapshot.SocialTargetDirection,
            SocialStance.Flee or SocialStance.KeepDistance => -snapshot.SocialTargetDirection,
            _ => 0.0f,
        };

        bool greet = false;
        if (stance == SocialStance.Greet)
        {
            greet = _lastGreetTick == int.MinValue ||
                    snapshot.Tick - _lastGreetTick >= band.GreetIntervalTicks;
            if (greet)
            {
                _lastGreetTick = snapshot.Tick;
            }
        }

        return new ActuationIntent(
            BehaviorPriority.Social,
            DriveActive: direction != 0.0f,
            WalkDirection: BlockedDirection(snapshot, direction),
            LocomotionScale: band.LocomotionScale,
            JumpRequested: false,
            GuardRequested: false,
            ResistGrab: false,
            Stance: stance,
            GreetRequested: greet);
    }

    private ActuationIntent AmbientIntent(in BehaviorSnapshot snapshot, in BuddyTraits traits)
    {
        if (!snapshot.AmbientDriveActive)
        {
            return ActuationIntent.Idle(BehaviorPriority.Ambient);
        }

        // Obstacle hop: propensity AND obstacle evidence AND stable support AND a committed
        // walk direction must all agree. Any one missing means no hop — this is what keeps
        // jumping from reading as "too random" (DECISIONS 2026-07-20).
        bool hop =
            snapshot.ObstacleInCommittedPath &&
            snapshot.HasStableSupport &&
            snapshot.AmbientWalkDirection != 0.0f &&
            traits.ObstacleHopPropensity >= _tuning.HopPropensityThreshold;

        return new ActuationIntent(
            BehaviorPriority.Ambient,
            DriveActive: true,
            WalkDirection: BlockedDirection(snapshot, snapshot.AmbientWalkDirection),
            LocomotionScale: snapshot.AmbientLocomotionScale,
            JumpRequested: hop,
            GuardRequested: false,
            ResistGrab: false,
            Stance: SocialStance.None,
            GreetRequested: false);
    }

    /// <summary>
    /// Zeroes a walk direction that would push into a blocked wall, preserving the accepted
    /// M1 wall-stop rule for every layer rather than only ambient autonomy.
    /// </summary>
    private static float BlockedDirection(in BehaviorSnapshot snapshot, float direction)
    {
        if (direction < 0.0f && snapshot.WallBlockedLeft)
        {
            return 0.0f;
        }

        if (direction > 0.0f && snapshot.WallBlockedRight)
        {
            return 0.0f;
        }

        return direction;
    }
}

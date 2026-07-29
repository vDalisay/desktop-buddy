using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Typed empirical tuning for sensing and bounded object actuation. Semantic
/// lifecycle durations are converted to the Godot-free domain tuning.
/// </summary>
[GlobalClass]
public partial class ObjectInteractionProfile : GameResource
{
    [Export(PropertyHint.Range, "16,512,1")] public float SenseRadius { get; set; } = 220.0f;
    /// <summary>
    /// Decision gate for an in-air catch: how close a flying object must be before the buddy
    /// puts its hands up. Not an arm length — <see cref="MaximumReach"/> bounds how far the
    /// hands actually go, so a generous value only makes the buddy react sooner.
    /// </summary>
    [Export(PropertyHint.Range, "1,256,0.5")] public float CatchDistance { get; set; } = 72.0f;

    /// <summary>
    /// Fast objects are impacts, not catches. This upper bound keeps ordinary player
    /// throws catchable while allowing a high-speed launcher strike to reach the buddy
    /// instead of being teleported into its hands.
    /// </summary>
    [Export(PropertyHint.Range, "1,5000,1")] public float MaximumCatchSpeed { get; set; } = 900.0f;

    /// <summary>
    /// Decision gate for a ground scoop, measured <b>horizontally</b>. The floor sits roughly
    /// `66 px` below the shoulder line, so a straight-line gate is only satisfiable once the
    /// feet are already kicking the object away — which is why the buddy used to shove balls
    /// into a corner instead of picking them up. The buddy can now walk right over the object
    /// it is committed to (collision exceptions apply from commitment), so this is a close
    /// "standing over it" range rather than a stop-before-contact range.
    /// </summary>
    [Export(PropertyHint.Range, "1,256,0.5")] public float ScoopDistance { get; set; } = 26.0f;

    [Export(PropertyHint.Range, "1,512,0.5")] public float ApproachDistance { get; set; } = 220.0f;

    /// <summary>
    /// How long an object the buddy just put down is left alone. Without this window the buddy
    /// re-commits to its own discard forever and never steps over anything.
    /// </summary>
    [Export(PropertyHint.Range, "0,1200,1")] public int ReleaseIgnoreTicks { get; set; } = 300;

    /// <summary>
    /// How high the carried object rides above its carrying hand, as a fraction of the hand and
    /// object radii. <c>1</c> rests it exactly on top of the hand.
    /// </summary>
    [Export(PropertyHint.Range, "0,2,0.05")] public float CarryLiftFraction { get; set; } = 1.0f;

    /// <summary>
    /// The carrying hand's pose relative to the torso, mirrored onto whichever hand holds the
    /// object. Kept near the hand's natural rest offset (`38, -5`) so the buddy simply stands
    /// there holding it, and far enough out that the object clears both torso and head — the
    /// only clear space on this rig is to the side, because the head's underside sits at `-26`
    /// and the torso's top at `-28` (owner correction 2026-07-27).
    /// </summary>
    [Export] public Vector2 CarryHandOffset { get; set; } = new(34.0f, -2.0f);

    /// <summary>How far the carrying hand draws back before the forward swing.</summary>
    [Export(PropertyHint.Range, "0,128,0.5")] public float ThrowWindupDistance { get; set; } = 26.0f;

    /// <summary>How far past the carry pose the hand swings before letting go.</summary>
    [Export(PropertyHint.Range, "0,128,0.5")] public float ThrowForwardDistance { get; set; } = 34.0f;

    /// <summary>Routed ticks of forward swing after the wind-up, ending in the release.</summary>
    [Export(PropertyHint.Range, "1,240,1")] public int ThrowForwardTicks { get; set; } = 8;

    /// <summary>
    /// How long a released object stays non-colliding with the buddy, so a thrown ball cannot
    /// clip the hand that threw it or the body it just left.
    /// </summary>
    [Export(PropertyHint.Range, "0,600,1")] public int ReleaseCollisionGraceTicks { get; set; } = 60;
    [Export(PropertyHint.Range, "1,600,1")] public int CatchTimeoutTicks { get; set; } = 90;
    [Export(PropertyHint.Range, "1,1200,1")] public int HoldTicks { get; set; } = 120;
    [Export(PropertyHint.Range, "1,1200,1")] public int InspectTicks { get; set; } = 150;

    /// <summary>Shoulder-line origin every reach is measured and clamped from.</summary>
    [Export] public Vector2 ReachOriginOffset { get; set; } = new(0.0f, -6.0f);

    /// <summary>
    /// Natural arm's length from <see cref="ReachOriginOffset"/>. The shipped rig rests its
    /// hands `38 px` out, so this is barely beyond a relaxed arm.
    /// </summary>
    [Export(PropertyHint.Range, "8,256,0.5")] public float ReachRadius { get; set; } = 44.0f;

    /// <summary>
    /// How far past <see cref="ReachRadius"/> a hand target may ever be placed. Every hand
    /// target is clamped into this circle, so no command can ask the arms to stretch across
    /// the room however far away the object is (owner correction 2026-07-26).
    /// </summary>
    [Export(PropertyHint.Range, "0,64,0.5")] public float MaximumReachExtension { get; set; } = 6.0f;

    /// <summary>Slack added to hand+object radii when testing whether the catch touched.</summary>
    [Export(PropertyHint.Range, "0,64,0.5")] public float CatchContactTolerance { get; set; } = 4.0f;

    /// <summary>Routed ticks the scoop dip plays before a resting object attaches.</summary>
    [Export(PropertyHint.Range, "1,240,1")] public int ScoopTicks { get; set; } = 24;

    /// <summary>Bounded downward force that dips the torso and head during a scoop.</summary>
    [Export(PropertyHint.Range, "0,100000,1")] public float ScoopDipForce { get; set; } = 2_400.0f;

    /// <summary>Routed ticks the hand draws back before the return throw releases.</summary>
    [Export(PropertyHint.Range, "1,240,1")] public int ThrowWindupTicks { get; set; } = 14;

    [Export(PropertyHint.Range, "0,128,0.5")] public float CatchHandClearance { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,128,0.5")] public float CatchConfirmDistance { get; set; } = 12.0f;
    /// <summary>
    /// How far a held object may separate from the hold centre before the grip counts as
    /// physically lost. Without this the hold is unconditional and nothing — a glove
    /// strike, a shove, a fall — can knock an object out of the buddy's hands, which also
    /// makes the model's interrupted-meal drop path unreachable (FR-008.10).
    /// </summary>
    [Export(PropertyHint.Range, "1,512,0.5")] public float HoldReleaseDistance { get; set; } = 72.0f;
    /// <summary>
    /// Carry pose relative to the torso. Raised toward the chest rather than the chin so the
    /// carried object clears the head (owner correction 2026-07-27).
    /// </summary>
    [Export] public Vector2 HoldCenterOffset { get; set; } = new(0.0f, -8.0f);
    [Export(PropertyHint.Range, "0,64,0.5")] public float HoldHandHalfSeparation { get; set; } = 15.0f;

    [Export(PropertyHint.Range, "0,100000,0.1")] public float HandStiffness { get; set; } = 1_800.0f;
    [Export(PropertyHint.Range, "0,10000,0.1")] public float HandDamping { get; set; } = 65.0f;
    /// <summary>
    /// Reduced from `18000` at owner correction 2026-07-26. Combined with the clamped
    /// reach envelope, the hands now ease out rather than being yanked to a target.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,200000,0.1")] public float MaximumHandForce { get; set; } = 6_000.0f;

    /// <summary>Forward distance ahead of the reach origin that counts as blocked.</summary>
    [Export(PropertyHint.Range, "1,256,0.5")] public float ObstacleForwardWindow { get; set; } = 52.0f;

    // Launch velocities in px/s, not impulses: the release assigns velocity directly so the
    // throw cannot be swallowed by the physics server's frozen-body handling.

    /// <summary>
    /// How long the return throw stays in the air. The launch is solved from this duration so
    /// the ball lands on the cursor (owner instruction 2026-07-27), which also guarantees the
    /// arc: the upward component always carries the whole fall.
    /// </summary>
    [Export(PropertyHint.Range, "0.05,3,0.05")] public float ThrowFlightSeconds { get; set; } = 0.55f;

    /// <summary>
    /// Speed ceiling for the solved throw. A cursor further away than the buddy can reach at
    /// this speed still gets an on-line throw that simply falls short.
    /// </summary>
    [Export(PropertyHint.Range, "0,10000,1")] public float TossSpeed { get; set; } = 720.0f;

    /// <summary>Fallback lift used only if the arc cannot be solved for the live body.</summary>
    [Export(PropertyHint.Range, "0,10000,1")] public float TossLiftSpeed { get; set; } = 240.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float DiscardSpeed { get; set; } = 180.0f;
    [Export(PropertyHint.Range, "0,10000,1")] public float DiscardLiftSpeed { get; set; } = 40.0f;

    /// <summary>
    /// Hand force during the throw gesture. The carry force is deliberately gentle, which is
    /// far too soft to swing an arm in a few ticks — the wind-up barely moved and the release
    /// read as a drop. The swing gets its own budget so the gesture actually plays.
    /// </summary>
    [Export(PropertyHint.Range, "0.1,200000,0.1")] public float ThrowHandForce { get; set; } = 24_000.0f;

    /// <summary>
    /// Ticks the launch velocity is re-asserted after release. One assignment can still be
    /// overwritten by the solver on the frame a body resumes simulation; re-stating it for a
    /// few ticks makes the throw deterministic.
    /// </summary>
    [Export(PropertyHint.Range, "1,60,1")] public int LaunchHoldTicks { get; set; } = 3;

    /// <summary>
    /// Routed ticks the toss gesture holds priority 5. Must exceed
    /// <see cref="ThrowWindupTicks"/> so the release beat actually happens.
    /// </summary>
    [Export(PropertyHint.Range, "2,600,1")] public int TossTicks { get; set; } = 20;

    public ObjectInteractionTuning ToDomainTuning() => new(
        CatchDistance,
        ApproachDistance,
        CatchTimeoutTicks,
        HoldTicks,
        InspectTicks,
        TossTicks,
        ScoopDistance);

    /// <summary>The absolute limit any hand target is clamped into.</summary>
    public float MaximumReach => ReachRadius + MaximumReachExtension;

    public bool IsRuntimeValid =>
        float.IsFinite(SenseRadius) && SenseRadius > 0.0f &&
        float.IsFinite(CatchDistance) && CatchDistance > 0.0f &&
        float.IsFinite(MaximumCatchSpeed) && MaximumCatchSpeed > 0.0f &&
        float.IsFinite(ScoopDistance) && ScoopDistance > 0.0f &&
        float.IsFinite(ApproachDistance) && ApproachDistance >= CatchDistance &&
        CatchTimeoutTicks > 0 && HoldTicks > 0 && InspectTicks > 0 &&
        ReleaseIgnoreTicks >= 0 &&
        float.IsFinite(CarryLiftFraction) && CarryLiftFraction >= 0.0f &&
        CarryHandOffset.IsFinite() &&
        float.IsFinite(ThrowWindupDistance) && ThrowWindupDistance >= 0.0f &&
        float.IsFinite(ThrowForwardDistance) && ThrowForwardDistance >= 0.0f &&
        ThrowForwardTicks > 0 && ReleaseCollisionGraceTicks >= 0 &&
        TossTicks > ThrowWindupTicks + ThrowForwardTicks &&
        ReachOriginOffset.IsFinite() &&
        float.IsFinite(ReachRadius) && ReachRadius > 0.0f &&
        float.IsFinite(MaximumReachExtension) && MaximumReachExtension >= 0.0f &&
        float.IsFinite(CatchContactTolerance) && CatchContactTolerance >= 0.0f &&
        ScoopTicks > 0 && ThrowWindupTicks > 0 && TossTicks > ThrowWindupTicks &&
        float.IsFinite(ScoopDipForce) && ScoopDipForce >= 0.0f &&
        float.IsFinite(ObstacleForwardWindow) && ObstacleForwardWindow > 0.0f &&
        float.IsFinite(CatchHandClearance) && CatchHandClearance >= 0.0f &&
        float.IsFinite(CatchConfirmDistance) && CatchConfirmDistance >= 0.0f &&
        float.IsFinite(HoldReleaseDistance) && HoldReleaseDistance > CatchConfirmDistance &&
        HoldCenterOffset.IsFinite() &&
        float.IsFinite(HoldHandHalfSeparation) && HoldHandHalfSeparation >= 0.0f &&
        float.IsFinite(HandStiffness) && HandStiffness >= 0.0f &&
        float.IsFinite(HandDamping) && HandDamping >= 0.0f &&
        float.IsFinite(MaximumHandForce) && MaximumHandForce > 0.0f &&
        float.IsFinite(ThrowFlightSeconds) && ThrowFlightSeconds > 0.0f &&
        float.IsFinite(TossSpeed) && TossSpeed >= 0.0f &&
        float.IsFinite(TossLiftSpeed) && TossLiftSpeed >= 0.0f &&
        float.IsFinite(DiscardSpeed) && DiscardSpeed >= 0.0f &&
        float.IsFinite(DiscardLiftSpeed) && DiscardLiftSpeed >= 0.0f &&
        DiscardSpeed <= TossSpeed;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        Positive(errors, SenseRadius, nameof(SenseRadius));
        Positive(errors, CatchDistance, nameof(CatchDistance));
        Positive(errors, MaximumCatchSpeed, nameof(MaximumCatchSpeed));
        Positive(errors, ApproachDistance, nameof(ApproachDistance));
        // ScoopDistance is horizontal and CatchDistance is straight-line, so they are not
        // comparable; each only has to be positive.
        Positive(errors, ScoopDistance, nameof(ScoopDistance));
        if (ApproachDistance < CatchDistance)
            errors.Add($"{nameof(ApproachDistance)} must be >= {nameof(CatchDistance)}");
        if (ReleaseIgnoreTicks < 0)
            errors.Add($"{nameof(ReleaseIgnoreTicks)} must be non-negative");
        NonNegative(errors, CarryLiftFraction, nameof(CarryLiftFraction));
        if (!CarryHandOffset.IsFinite()) errors.Add($"{nameof(CarryHandOffset)} must be finite");
        NonNegative(errors, ThrowWindupDistance, nameof(ThrowWindupDistance));
        NonNegative(errors, ThrowForwardDistance, nameof(ThrowForwardDistance));
        if (ThrowForwardTicks <= 0) errors.Add($"{nameof(ThrowForwardTicks)} must be positive");
        if (ReleaseCollisionGraceTicks < 0)
            errors.Add($"{nameof(ReleaseCollisionGraceTicks)} must be non-negative");
        if (TossTicks <= ThrowWindupTicks + ThrowForwardTicks)
        {
            errors.Add(
                $"{nameof(TossTicks)} must exceed {nameof(ThrowWindupTicks)} + " +
                $"{nameof(ThrowForwardTicks)} so the release beat happens inside the gesture");
        }
        if (CatchTimeoutTicks <= 0) errors.Add($"{nameof(CatchTimeoutTicks)} must be positive");
        if (HoldTicks <= 0) errors.Add($"{nameof(HoldTicks)} must be positive");
        if (InspectTicks <= 0) errors.Add($"{nameof(InspectTicks)} must be positive");
        NonNegative(errors, CatchHandClearance, nameof(CatchHandClearance));
        NonNegative(errors, CatchConfirmDistance, nameof(CatchConfirmDistance));
        if (!float.IsFinite(HoldReleaseDistance) || HoldReleaseDistance <= CatchConfirmDistance)
        {
            errors.Add(
                $"{nameof(HoldReleaseDistance)} must be finite and greater than " +
                $"{nameof(CatchConfirmDistance)}");
        }
        if (!HoldCenterOffset.IsFinite()) errors.Add($"{nameof(HoldCenterOffset)} must be finite");
        if (!ReachOriginOffset.IsFinite()) errors.Add($"{nameof(ReachOriginOffset)} must be finite");
        Positive(errors, ReachRadius, nameof(ReachRadius));
        NonNegative(errors, MaximumReachExtension, nameof(MaximumReachExtension));
        NonNegative(errors, CatchContactTolerance, nameof(CatchContactTolerance));
        if (ScoopTicks <= 0) errors.Add($"{nameof(ScoopTicks)} must be positive");
        if (ThrowWindupTicks <= 0) errors.Add($"{nameof(ThrowWindupTicks)} must be positive");
        if (TossTicks <= ThrowWindupTicks)
            errors.Add($"{nameof(TossTicks)} must exceed {nameof(ThrowWindupTicks)}");
        NonNegative(errors, ScoopDipForce, nameof(ScoopDipForce));
        Positive(errors, ObstacleForwardWindow, nameof(ObstacleForwardWindow));
        NonNegative(errors, HoldHandHalfSeparation, nameof(HoldHandHalfSeparation));
        NonNegative(errors, HandStiffness, nameof(HandStiffness));
        NonNegative(errors, HandDamping, nameof(HandDamping));
        Positive(errors, MaximumHandForce, nameof(MaximumHandForce));
        Positive(errors, ThrowFlightSeconds, nameof(ThrowFlightSeconds));
        NonNegative(errors, TossSpeed, nameof(TossSpeed));
        NonNegative(errors, TossLiftSpeed, nameof(TossLiftSpeed));
        NonNegative(errors, DiscardSpeed, nameof(DiscardSpeed));
        NonNegative(errors, DiscardLiftSpeed, nameof(DiscardLiftSpeed));
        if (DiscardSpeed > TossSpeed)
            errors.Add($"{nameof(DiscardSpeed)} must not exceed {nameof(TossSpeed)}");
        return errors;
    }

    private static void Positive(Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0.0f)
            errors.Add($"{name} must be finite and positive");
    }

    private static void NonNegative(Godot.Collections.Array<string> errors, float value, string name)
    {
        if (!float.IsFinite(value) || value < 0.0f)
            errors.Add($"{name} must be finite and non-negative");
    }
}

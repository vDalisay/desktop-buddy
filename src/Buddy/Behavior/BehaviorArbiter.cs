using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Thin Godot adapter around the pure §4 ladder. It snapshots focused
/// producers, pauses suppressed autonomy, and translates the winning semantic
/// layer into the one rich <see cref="DriveIntent"/> consumed by ActiveDrive.
/// </summary>
[GlobalClass]
public partial class BehaviorArbiter : Node
{
    private bool _allocationProbeEnabled;
    private BehaviorArbiterModel _model = null!;
    private BuddyProgressState? _progress;
    private BuddyTraits _savelessTraits = BuddyTraits.Default;
    private MoodBand _savelessMoodBand = MoodBand.Neutral;
    private bool _statusHazard;
    private float _statusHazardFleeDirection = 1.0f;

    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public StandingDetector Standing { get; set; } = null!;
    [Export] public RecoveryComponent Recovery { get; set; } = null!;
    [Export] public AutonomousMotionComponent AutonomousMotion { get; set; } = null!;
    [Export] public ActiveDriveComponent ActiveDrive { get; set; } = null!;
    [Export] public GrabResistanceComponent GrabResistance { get; set; } = null!;
    [Export] public BehaviorActivityComponent Activity { get; set; } = null!;
    [Export] public ObjectInteractionComponent ObjectInteraction { get; set; } = null!;
    [Export] public BehaviorArbiterProfile Profile { get; set; } = null!;

    public bool IsInitialized { get; private set; }
    public ActuationIntent Intent { get; private set; }
    public DriveIntent DriveIntent { get; private set; }
    public ArbiterDiagnostics Diagnostics =>
        IsInitialized ? _model.Diagnostics : default;
    public SocialTuningSet SocialTuning =>
        Profile is not null && GodotObject.IsInstanceValid(Profile)
            ? Profile.ToSocialTuning()
            : SocialTuningSet.Default;

    public void Initialize(BuddyProgressState progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _progress = progress;
        InitializeCore();
    }

    public void InitializeSaveless(
        BuddyTraits? traits = null,
        MoodBand moodBand = MoodBand.Neutral)
    {
        _progress = null;
        _savelessTraits = traits ?? BuddyTraits.Default;
        _savelessMoodBand = moodBand;
        InitializeCore();
    }

    private void InitializeCore()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Standing) || !Standing.IsInitialized ||
            !GodotObject.IsInstanceValid(Recovery) || !Recovery.IsInitialized ||
            !GodotObject.IsInstanceValid(AutonomousMotion) || !AutonomousMotion.IsInitialized ||
            !GodotObject.IsInstanceValid(ActiveDrive) || !ActiveDrive.IsInitialized ||
            !GodotObject.IsInstanceValid(GrabResistance) || !GrabResistance.IsInitialized ||
            !GodotObject.IsInstanceValid(Activity) || !Activity.IsInitialized ||
            !GodotObject.IsInstanceValid(ObjectInteraction) ||
            !GodotObject.IsInstanceValid(Profile) || !Profile.IsRuntimeValid)
        {
            throw new InvalidOperationException(
                "BehaviorArbiter requires every initialized behavior/drive dependency and a valid profile.");
        }

        _model = new BehaviorArbiterModel(
            Profile.ToDomainTuning(),
            Profile.ToSocialTuning());
        Intent = default;
        DriveIntent = default;
        IsInitialized = true;
    }

    public DriveIntent PhysicsTick(
        long routedTick,
        Consciousness consciousness,
        in ToolReactionIntent toolReaction,
        BuddyPartId? grabbedPart,
        bool dangled,
        bool hardRecoveredThisTick,
        Vector2 cursorWorldPosition,
        bool socialTargetValid)
    {
        long allocationBefore = _allocationProbeEnabled
            ? GC.GetAllocatedBytesForCurrentThread()
            : 0;

        if (!IsInitialized)
            throw new InvalidOperationException("BehaviorArbiter was ticked before per-run injection.");

        if (hardRecoveredThisTick)
            _model.Reset();

        GrabResistanceIntent resistance = GrabResistance.Intent;
        bool supportContact = Standing.Snapshot.SupportContactCount > 0;
        bool toolHazard = toolReaction.Active && toolReaction.GuardActive;
        bool hazard = toolHazard || _statusHazard;

        AutonomousMotionIntent ambient = AutonomousMotion.Intent;
        float targetOffsetX = cursorWorldPosition.X - Rig.Torso.GlobalPosition.X;
        float targetDirection = Mathf.IsZeroApprox(targetOffsetX)
            ? 1.0f
            : Mathf.Sign(targetOffsetX);
        MoodBand moodBand = _progress?.MoodBand ?? _savelessMoodBand;
        BuddyTraits traits = _progress?.Traits ?? _savelessTraits;
        bool socialReaction = toolReaction.Active && !toolReaction.GuardActive;

        // Snapshot first, object decision second, arbitration third. The object worker
        // must know whether a priority above 5 is eligible before it ticks, and asking
        // the model that question keeps one ladder instead of a hand-rolled copy that
        // must be kept in sync (the object fields below are filled in afterwards).
        var snapshot = new BehaviorSnapshot(
            Tick: unchecked((int)Math.Min(routedTick, int.MaxValue)),
            Consciousness: consciousness,
            RequiresFailsafeReposition: hardRecoveredThisTick,
            SelfRightingEligible: Recovery.State.AssistanceActive,
            HazardPresent: hazard,
            // A learned-harm guard flee already carries its own direction; a status hazard
            // supplies one only when nothing else does.
            HazardFleeDirection: toolHazard
                ? toolReaction.WalkDirection
                : _statusHazardFleeDirection,
            Grabbed: grabbedPart is not null,
            AfraidOfGrab: resistance.Active,
            GrabFleeDirection: resistance.Direction,
            HasStableSupport: Standing.Snapshot.IsStable,
            WallBlockedLeft: AutonomousMotion.BlockedLeft,
            WallBlockedRight: AutonomousMotion.BlockedRight,
            MoodBand: moodBand,
            ObjectActionCommitted: false,
            ObjectApproachDirection: 0.0f,
            SocialTargetValid: socialTargetValid,
            SocialTargetDirection: targetDirection,
            SocialTargetDistance: Mathf.Abs(targetOffsetX),
            AmbientDriveActive: true,
            AmbientWalkDirection: ambient.WalkDirection,
            AmbientLocomotionScale: 1.0f,
            // Two independent sources on purpose: the layer-3 ray, and the registry's own
            // view of resting objects in the path. The ray alone was intermittent.
            ObstacleInCommittedPath:
                AutonomousMotion.ObstacleInCommittedPath(ambient.WalkDirection) ||
                ObjectInteraction.RestingObstacleInPath(ambient.WalkDirection),
            HasSupportContact: supportContact,
            SocialReactionPresent: socialReaction,
            WallContactLeft: AutonomousMotion.ContactLeft,
            WallContactRight: AutonomousMotion.ContactRight);

        ObjectInteraction.PhysicsTick(
            BehaviorArbiterModel.SuppressesVoluntaryAction(snapshot),
            consciousness == Consciousness.Conscious,
            cursorWorldPosition);

        snapshot = snapshot with
        {
            ObjectActionCommitted =
                ObjectInteraction.Phase != ObjectPhase.Idle ||
                Activity.Current == ActivityId.Eat,
            ObjectApproachDirection = ObjectInteraction.ApproachDirection,
        };

        Intent = _model.Resolve(snapshot, traits);

        if (Activity.Current == ActivityId.Wave &&
            Intent.Owner < BehaviorPriority.Social)
        {
            Activity.Interrupt();
        }

        bool ambientOwns = Intent.Owner == BehaviorPriority.Ambient;
        AutonomousMotion.PhysicsTick(
            consciousness,
            Recovery.State,
            behaviorEnabled: ambientOwns && !Activity.IsStationary && !dangled);

        if (Intent.Owner == BehaviorPriority.Social &&
            Intent.GreetRequested &&
            Activity.Current == ActivityId.None)
        {
            Activity.SetActivity(ActivityId.Wave);
        }

        DriveIntent = BuildDriveIntent(routedTick, grabbedPart, toolReaction);
        if (_allocationProbeEnabled)
        {
            AllocationSamples++;
            AllocatedBytes +=
                GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        }
        return DriveIntent;
    }

    public int AllocationSamples { get; private set; }
    public long AllocatedBytes { get; private set; }

    public void BeginAllocationProbe()
    {
        AllocationSamples = 0;
        AllocatedBytes = 0;
        _allocationProbeEnabled = true;
    }

    public void EndAllocationProbe() => _allocationProbeEnabled = false;

    /// <summary>
    /// A status effect the buddy carries — today only Burning (RAGDOLL §9.3) — is an
    /// immediate hazard in its own right, with no tool contact involved. The composition
    /// root sets it from the sprayer's burn state each routed tick; the ladder does the
    /// rest, because priority 3 already outranks a committed object action and the social
    /// and ambient layers.
    /// </summary>
    public void SetStatusHazard(bool present, float fleeDirection)
    {
        _statusHazard = present;
        if (present && (fleeDirection > 0.0f || fleeDirection < 0.0f))
            _statusHazardFleeDirection = Mathf.Sign(fleeDirection);
    }

    public void Reset()
    {
        if (!IsInitialized)
            return;
        _statusHazard = false;
        _model.Reset();
        Intent = default;
        DriveIntent = default;
    }

    private DriveIntent BuildDriveIntent(
        long routedTick,
        BuddyPartId? grabbedPart,
        in ToolReactionIntent toolReaction)
    {
        if (Intent.Owner == BehaviorPriority.GrabResistance)
            return BuildResistanceIntent(grabbedPart);

        if (Intent.Owner == BehaviorPriority.Hazard && _statusHazard)
            return BuildBurningHazardIntent(routedTick);

        if (Intent.Owner is BehaviorPriority.Hazard or BehaviorPriority.Social &&
            toolReaction.Active)
        {
            return BuildToolReactionIntent(toolReaction);
        }

        if (Intent.Owner == BehaviorPriority.ObjectAction)
            return BuildObjectIntent();

        // A status hazard has no tool reaction behind it, so the guard-shaped intent above
        // does not apply: the buddy simply runs, which is the panic.
        if (Intent.Owner == BehaviorPriority.Hazard ||
            Intent.Owner == BehaviorPriority.Social)
        {
            return BasicDrive(Intent.WalkDirection, Intent.LocomotionScale);
        }

        if (Intent.Owner == BehaviorPriority.Ambient)
        {
            // Intent.WalkDirection, not the raw planner direction: the arbiter's wall
            // filter must reach every layer, or it silently applies to none of them.
            //
            // An obstacle hop carries forward momentum along the committed walk. Passing
            // zero here made the hop purely vertical, so the buddy launched straight up and
            // landed back on the same object (owner correction 2026-07-26).
            return new DriveIntent(
                Intent.DriveActive ? Intent.WalkDirection : 0.0f,
                Intent.LocomotionScale,
                Intent.JumpRequested,
                Intent.WalkDirection,
                1.0f,
                Profile.ObstacleHopHorizontalRatio,
                0.0f,
                0.0f,
                false,
                Vector2.Zero,
                Vector2.Zero,
                0.0f,
                0.0f,
                0.0f,
                1.0f,
                AutonomousMotion.IsWallStopping,
                false,
                0.0f,
                Vector2.Zero,
                Vector2.Zero,
                false,
                false,
                Vector2.Zero,
                Vector2.Zero);
        }

        return default;
    }

    private DriveIntent BuildResistanceIntent(BuddyPartId? grabbedPart)
    {
        GrabResistanceIntent resistance = GrabResistance.Intent;
        ActiveDriveProfile drive = ActiveDrive.Profile;
        var tuning = new PanicFlailTuning(
            drive.PanicFlailAmplitude,
            drive.PanicFlailLift,
            drive.PanicFlailCycleTicks,
            drive.PanicFlailAsymmetry,
            drive.PanicFlailReachBias);
        PanicFlailSample flail = PanicFlail.Sample(
            GrabResistance.ResistTicks,
            resistance.Strength,
            resistance.Direction,
            tuning);
        Vector2 shoulder = Rig.Torso.GlobalPosition + drive.PanicHandOriginOffset;

        return new DriveIntent(
            resistance.Direction,
            drive.GrabResistanceWalkScale,
            false, 0.0f, 1.0f, 0.0f,
            resistance.Direction, resistance.Strength,
            false, Vector2.Zero, Vector2.Zero, 0.0f, 0.0f, 0.0f, 1.0f,
            false, false, 0.0f, Vector2.Zero, Vector2.Zero,
            PanicLeftHandActive: grabbedPart != BuddyPartId.LeftHand,
            PanicRightHandActive: grabbedPart != BuddyPartId.RightHand,
            LeftPanicHandTarget: shoulder + ToGodot(flail.LeftHandOffset),
            RightPanicHandTarget: shoulder + ToGodot(flail.RightHandOffset));
    }

    private DriveIntent BuildBurningHazardIntent(long routedTick)
    {
        ActiveDriveProfile drive = ActiveDrive.Profile;
        var tuning = new PanicFlailTuning(
            drive.PanicFlailAmplitude,
            drive.PanicFlailLift,
            drive.PanicFlailCycleTicks,
            drive.PanicFlailAsymmetry,
            drive.PanicFlailReachBias);
        PanicFlailSample flail = PanicFlail.Sample(
            unchecked((int)routedTick), 1.0f, Intent.WalkDirection, tuning);
        Vector2 shoulder = Rig.Torso.GlobalPosition + drive.PanicHandOriginOffset;

        return BasicDrive(Intent.WalkDirection, Profile.BurningLocomotionScale) with
        {
            PanicLeftHandActive = true,
            PanicRightHandActive = true,
            LeftPanicHandTarget = shoulder + ToGodot(flail.LeftHandOffset),
            RightPanicHandTarget = shoulder + ToGodot(flail.RightHandOffset),
        };
    }

    private static DriveIntent BuildToolReactionIntent(in ToolReactionIntent reaction) =>
        new(
            reaction.WalkDirection,
            reaction.LocomotionScale,
            reaction.JumpRequested,
            reaction.JumpDirection,
            reaction.JumpScale,
            reaction.JumpHorizontalRatio,
            0.0f,
            0.0f,
            reaction.GuardActive,
            reaction.LeftGuardTarget,
            reaction.RightGuardTarget,
            reaction.GuardStiffness,
            reaction.GuardDamping,
            reaction.GuardMaximumForce,
            reaction.GuardAbsorption,
            false,
            false,
            0.0f,
            Vector2.Zero,
            Vector2.Zero,
            false,
            false,
            Vector2.Zero,
            Vector2.Zero);

    private DriveIntent BuildObjectIntent()
    {
        bool reach = Activity.EatReachActive;
        Vector2 leftTarget = Vector2.Zero;
        Vector2 rightTarget = Vector2.Zero;
        if (reach)
        {
            Vector2 chest = Rig.Torso.GlobalPosition + ActiveDrive.Profile.EatChestTargetOffset;
            Vector2 lower = Rig.Torso.GlobalPosition + ActiveDrive.Profile.EatFinalLowerTargetOffset;
            Vector2 returnCenter = chest.Lerp(lower, Activity.EatFinalLowering);
            Vector2 mouth = Rig.Head.GlobalPosition + ActiveDrive.Profile.EatMouthTargetOffset;
            Vector2 reachCenter = returnCenter.Lerp(mouth, Activity.EatLift);
            Vector2 separation = new(ActiveDrive.Profile.EatHandHalfSeparation, 0.0f);
            leftTarget = reachCenter - separation;
            rightTarget = reachCenter + separation;
        }

        return new DriveIntent(
            Intent.WalkDirection,
            Intent.LocomotionScale,
            false, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f,
            false, Vector2.Zero, Vector2.Zero, 0.0f, 0.0f, 0.0f, 1.0f,
            Activity.IsStationary || ObjectInteraction.Phase != ObjectPhase.Approach,
            reach,
            Activity.EatLift,
            leftTarget,
            rightTarget,
            false,
            false,
            Vector2.Zero,
            Vector2.Zero,
            ObjectInteraction.CurrentDriveCommand);
    }

    private static DriveIntent BasicDrive(float direction, float scale) =>
        new(
            direction, scale,
            false, 0.0f, 1.0f, 0.0f,
            0.0f, 0.0f,
            false, Vector2.Zero, Vector2.Zero, 0.0f, 0.0f, 0.0f, 1.0f,
            false, false, 0.0f, Vector2.Zero, Vector2.Zero,
            false, false, Vector2.Zero, Vector2.Zero);

    private static Vector2 ToGodot(System.Numerics.Vector2 value) =>
        new(value.X, value.Y);
}

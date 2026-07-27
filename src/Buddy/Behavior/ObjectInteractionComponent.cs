using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Objects;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>
/// Area-backed loose-object sensing and runtime lifecycle integration. The pure
/// <see cref="ObjectInteractionModel"/> decides semantic transitions; this worker
/// resolves fixed-buffer candidates, hold feedback, care transactions, collision
/// exceptions, and bounded runtime commands for <see cref="ActiveDriveComponent"/>.
/// </summary>
[GlobalClass]
public partial class ObjectInteractionComponent : Area2D
{
    private readonly LooseObjectBody?[] _sensed = new LooseObjectBody?[LooseObjectRegistry.Capacity];
    private readonly ObjectCandidate[] _candidates = new ObjectCandidate[LooseObjectRegistry.Capacity];
    private readonly CareConsumableModel _consumables = new();

    private LooseObjectRegistry _registry = null!;
    private BuddyProgressState _progress = null!;
    private Func<string, bool> _isHarmful = null!;
    private ObjectInteractionModel _model = null!;
    private LooseObjectBody? _heldBody;
    private int _consumeToken;
    private bool _consumeCompleted;
    private bool _directLabConsume;
    private bool _collisionExceptionsActive;
    private bool _skipNextCooldownTick;
    private int _catchTicks;
    private int _throwWindupTicks;
    private bool _throwReleased;
    private bool _catchWasClean;
    private bool _pendingLeftHandAttach;
    private bool _attachedToLeftHand;
    private LooseObjectBody? _exceptionBody;
    private int _exceptionGraceTicks;
    private float _heldLinearDamp;
    private float _heldAngularDamp;
    private Vector2 _cursorWorldPosition;
    private LooseObjectBody? _launchBody;
    private Vector2 _launchVelocity;
    private int _launchTicksRemaining;
    private float _defaultGravity = 980.0f;

    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public BehaviorActivityComponent Activity { get; set; } = null!;
    [Export] public ObjectInteractionProfile Profile { get; set; } = null!;

    public event Action<LooseObjectBody>? ConsumeStarted;
    public event Action<LooseObjectBody>? ConsumeCancelled;
    public event Action<LooseObjectBody>? ConsumeSucceeded;

    /// <summary>
    /// Raised on the tick the buddy catches a player-thrown object out of the air and still
    /// finds catch fun. Presentation turns this into the laugh (owner instruction
    /// 2026-07-27); a catch off the floor, or one by a buddy bored of catch, raises nothing.
    /// </summary>
    public event Action? FunCatchDelighted;

    public bool IsInitialized { get; private set; }
    public int SensedCount { get; private set; }
    public ObjectPhase Phase => _directLabConsume
        ? ObjectPhase.Consume
        : _model is null ? ObjectPhase.Idle : _model.Phase;
    public int TrackedRuntimeId => _heldBody?.RuntimeId ?? _model?.TrackedRuntimeId ?? 0;
    public bool IsHolding => GodotObject.IsInstanceValid(_heldBody);
    public bool CollisionExceptionsActive => _collisionExceptionsActive;
    public bool FleeBiasRequested { get; private set; }
    public float ApproachDirection { get; private set; }
    public int CatchCareCount { get; private set; }

    /// <summary>Catches taken out of the air off a player throw, fun or not.</summary>
    public int CleanCatchCount { get; private set; }

    /// <summary>Clean catches the buddy actually enjoyed, so laughed about.</summary>
    public int FunCatchCount { get; private set; }

    /// <summary>Catch novelty left after the most recent clean catch, for diagnostics.</summary>
    public float LastCatchInterest { get; private set; } = FunInterestModel.MaximumInterest;
    public int TossCount { get; private set; }
    public int DiscardCount { get; private set; }
    public int DropCount { get; private set; }
    public int ConsumeSuccessCount { get; private set; }
    public int ConsumeCancelCount { get; private set; }
    public ConsumeRejection LastConsumeRejection { get; private set; }
    public ObjectDriveCommand CurrentDriveCommand { get; private set; }
    public Vector2 LastReleaseImpulse { get; private set; }

    /// <summary>Where the last release happened, so aim can be judged from the throwing hand.</summary>
    public Vector2 LastReleaseOrigin { get; private set; }

    /// <summary>True while the held object is attached to a hand socket.</summary>
    public bool IsAttached { get; private set; }

    /// <summary>
    /// The cursor position this component last aimed against. Facing reads it so the buddy
    /// turns toward the player while it is holding something (owner instruction 2026-07-27).
    /// </summary>
    public Vector2 CursorWorldPosition => _cursorWorldPosition;

    /// <summary>
    /// The object the buddy is currently paying attention to — the one it has committed to,
    /// including one the player is still carrying. Presentation reads this so the head tracks
    /// the ball you are about to throw instead of staring past it.
    /// </summary>
    public bool HasWatchTarget { get; private set; }
    public Vector2 WatchTargetPosition { get; private set; }

    /// <summary>Largest distance any commanded hand target sat from the reach origin.</summary>
    public float MaximumCommandedReach { get; private set; }

    /// <summary>
    /// Registry-backed obstacle evidence: a resting object sitting in the committed path
    /// below the torso. The layer-3 ray misses a ball the buddy is already touching, so
    /// this is the reliable half of the hop gate (owner correction 2026-07-26).
    /// </summary>
    public bool RestingObstacleLeft { get; private set; }
    public bool RestingObstacleRight { get; private set; }

    public bool RestingObstacleInPath(float direction) =>
        direction < 0.0f ? RestingObstacleLeft :
        direction > 0.0f && RestingObstacleRight;

    private Vector2 ReachOrigin => Rig.Torso.GlobalPosition + Profile.ReachOriginOffset;

    public int CooldownTicksRemaining(string contentId) =>
        _consumables.CooldownTicksRemaining(contentId);

    public void Initialize(
        LooseObjectRegistry registry,
        BuddyProgressState progress,
        SocialTuningSet? socialTuning = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(progress);
        bool profileValid = GodotObject.IsInstanceValid(Profile) && Profile.IsRuntimeValid;
        if (!registry.IsInitialized ||
            !GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Activity) || !Activity.IsInitialized ||
            !profileValid)
        {
            throw new InvalidOperationException(
                "ObjectInteractionComponent requires initialized registry, rig, activity, and profile.");
        }

        ValidateSensorWiring();
        CollisionShape2D? shapeNode = null;
        for (int index = 0; index < GetChildCount(); index++)
        {
            if (GetChild(index) is CollisionShape2D candidate)
            {
                shapeNode = candidate;
                break;
            }
        }
        if (shapeNode?.Shape is not CircleShape2D circle)
            throw new InvalidOperationException("Object interaction sensor requires a circle CollisionShape2D.");
        circle.Radius = Profile.SenseRadius;

        // The throw solver needs the same gravity the objects actually fall under, so read the
        // project's own value rather than assuming the engine default.
        _defaultGravity = ProjectSettings
            .GetSetting("physics/2d/default_gravity", 980.0f)
            .AsSingle();

        _registry = registry;
        _progress = progress;
        _isHarmful = progress.IsContentHarmful;
        _model = new ObjectInteractionModel(Profile.ToDomainTuning(), socialTuning);
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
        Activity.EatBiteCompleted += OnEatBiteCompleted;
        Activity.ActivityChanged += OnActivityChanged;
        IsInitialized = true;
    }

    public void ValidateSensorWiring()
    {
        StartupCheck check = StartupValidator.ValidateInteractionSense(this);
        if (!check.Passed)
        {
            throw new InvalidOperationException(
                $"Object interaction sensor must be layer={CollisionLayers.InteractionSense}, " +
                $"mask={CollisionLayers.MaskInteractionSense}, monitoring=true, monitorable=false; " +
                $"actual {check.Detail}.");
        }
    }

    /// <summary>Called once by BuddyRoot from the routed fixed tick.</summary>
    public void PhysicsTick(bool suppressed, bool conscious, Vector2 cursorWorldPosition)
    {
        if (!IsInitialized)
            return;

        GlobalPosition = Rig.Torso.GlobalPosition;
        FleeBiasRequested = false;
        _cursorWorldPosition = cursorWorldPosition;
        HoldLaunchVelocity();
        FollowAttachedHand();
        if (_directLabConsume)
        {
            if (suppressed || !conscious || !HoldStillIntact())
            {
                CancelActiveInteraction();
                TickCooldowns();
                return;
            }
            CurrentDriveCommand = BuildHoldCommand(_heldBody, ObjectDriveAction.Hold);
            TickCooldowns();
            return;
        }

        int count = BuildCandidates();
        bool holdConfirmed = IsHolding ? HoldStillIntact() : ResolveCatch();

        ObjectIntent intent = _model.Tick(
            _candidates.AsSpan(0, count),
            _progress.MoodBand,
            _isHarmful,
            suppressed,
            conscious,
            holdConfirmed,
            _consumeCompleted);
        _consumeCompleted = false;
        HandleIntent(intent, cursorWorldPosition);

        // Keep the buddy from colliding with whatever it is currently going for, and drop the
        // exception the moment it stops caring about it.
        LooseObjectBody? committed = IsHolding
            ? _heldBody
            : _model.IsCommitted ? _registry.FindBody(_model.TrackedRuntimeId) : null;
        ResolveCommitExceptions(committed);

        HasWatchTarget = !IsHolding && GodotObject.IsInstanceValid(committed);
        WatchTargetPosition = HasWatchTarget ? committed!.GlobalPosition : Vector2.Zero;
        TickCooldowns();
    }

    /// <summary>
    /// Debug-laboratory compatibility path for E: the key now opens the real
    /// care transaction on a registered lab-food body and succeeds only on bite five.
    /// </summary>
    public bool TryBeginLaboratoryFoodConsume(LooseObjectBody body)
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(body) ||
            body.SemanticContentId != ContentIds.CareLabFood)
        {
            LastConsumeRejection = ConsumeRejection.UnknownConsumable;
            return false;
        }

        // Cooldown is a property of the content ID, not of what the hands happen to be
        // doing, so it is the answer whenever it applies. Reporting the hand state first
        // hid the real reason behind UnknownConsumable once the buddy started engaging
        // objects on its own.
        if (_consumables.IsOnCooldown(ContentIds.CareLabFood))
        {
            LastConsumeRejection = ConsumeRejection.OnCooldown;
            return false;
        }

        if (IsHolding ||
            !_registry.TryGetSnapshot(body.RuntimeId, out LooseObjectSnapshot snapshot) ||
            !snapshot.Consumable)
        {
            LastConsumeRejection = ConsumeRejection.UnknownConsumable;
            return false;
        }

        if (!_consumables.TryBegin(
            ContentIds.CareLabFood, out _consumeToken, out ConsumeRejection rejection))
        {
            LastConsumeRejection = rejection;
            return false;
        }

        LastConsumeRejection = ConsumeRejection.None;
        _directLabConsume = true;
        BeginHold(
            body,
            Rig.LeftHand.GlobalPosition.DistanceTo(body.GlobalPosition) <=
                Rig.RightHand.GlobalPosition.DistanceTo(body.GlobalPosition));
        Activity.SetActivity(ActivityId.Eat);
        ConsumeStarted?.Invoke(body);
        Log.Info("ObjectInteraction",
            $"Lab food consume started runtime={body.RuntimeId} cooldown_before=0.");
        return true;
    }

    public void CancelActiveInteraction()
    {
        if (!IsInitialized)
            return;
        CancelConsume();
        _model.Reset();
        ReleaseHeld(ObjectDriveAction.Drop, Vector2.Zero);
        CurrentDriveCommand = ObjectDriveCommand.None;
    }

    public void Reset()
    {
        CancelActiveInteraction();
        _consumables.Reset();
        Array.Clear(_sensed);
        SensedCount = 0;
        _catchTicks = 0;
        _throwWindupTicks = 0;
        _throwReleased = false;
        _exceptionGraceTicks = 0;
        ApplyCommitExceptions(null);
        RestingObstacleLeft = false;
        RestingObstacleRight = false;
        MaximumCommandedReach = 0.0f;
    }

    /// <summary>Clears the reach telemetry high-water mark between scenario phases.</summary>
    public void ResetReachTelemetry() => MaximumCommandedReach = 0.0f;

    private int BuildCandidates()
    {
        int count = 0;
        Vector2 origin = ReachOrigin;
        float torsoY = Rig.Torso.GlobalPosition.Y;
        RestingObstacleLeft = false;
        RestingObstacleRight = false;
        for (int index = 0; index < _sensed.Length; index++)
        {
            LooseObjectBody? body = _sensed[index];
            if (body is null)
                continue;
            if (!GodotObject.IsInstanceValid(body))
            {
                // Consumed or evicted without an exit signal; release the slot here so
                // capacity and SensedCount cannot drift over a long session.
                _sensed[index] = null;
                SensedCount = Mathf.Max(0, SensedCount - 1);
                continue;
            }
            // A player-held object is deliberately included. Skipping it left the buddy blind
            // to the ball until the instant it was released, which is far too late to react to
            // a close throw — it is why the buddy appeared to ignore balls thrown at it.
            if (!_registry.TryGetSnapshot(body.RuntimeId, out LooseObjectSnapshot snapshot) ||
                snapshot.BuddyHeld)
            {
                continue;
            }

            Vector2 offset = body.GlobalPosition - origin;
            // A true 2D reach distance. Scoring on |dx| alone admitted an object 46 px
            // sideways and arbitrarily far above, which is how the arms ended up stretched
            // across the room (owner correction 2026-07-26).
            _candidates[count++] = new ObjectCandidate(
                snapshot.RuntimeId,
                snapshot.ContentId,
                snapshot.ThrowToken,
                offset.Length(),
                Mathf.IsZeroApprox(offset.X) ? 1.0f : Mathf.Sign(offset.X),
                snapshot.Consumable,
                snapshot.AtRest,
                snapshot.Ignored,
                Mathf.Abs(offset.X),
                snapshot.PlayerHeld);

            if (snapshot.AtRest && !snapshot.PlayerHeld &&
                body.GlobalPosition.Y > torsoY &&
                Mathf.Abs(offset.X) <= Profile.ObstacleForwardWindow)
            {
                if (offset.X < 0.0f)
                    RestingObstacleLeft = true;
                else
                    RestingObstacleRight = true;
            }
        }
        return count;
    }

    /// <summary>
    /// Decides whether the tracked object is caught this tick. An airborne object confirms
    /// when it physically touches a hand; a resting one confirms at the end of the scoop dip.
    /// The rest state comes from the registry, so the domain lifecycle stays flavour-agnostic.
    /// </summary>
    /// <summary>
    /// Whether the tracked object is being scooped off the ground rather than caught in the
    /// air. The model decides this when it commits, so the flavour cannot flip mid-pickup
    /// just because the buddy's own feet nudged the ball into motion.
    /// </summary>
    private bool IsScooping => _model.TrackedAtRest;

    private bool ResolveCatch()
    {
        LooseObjectBody? tracked = _registry.FindBody(_model.TrackedRuntimeId);
        if (!GodotObject.IsInstanceValid(tracked))
        {
            _catchTicks = 0;
            return false;
        }

        // The player still has it: hold the ready pose, do not take it out of their hand.
        bool known = _registry.TryGetSnapshot(tracked!.RuntimeId, out LooseObjectSnapshot live);
        if (known && live.PlayerHeld)
        {
            _catchTicks = 0;
            return false;
        }

        if (IsScooping)
        {
            // Scoop: dip for a beat, then the object relocates into the hands. It is a timed
            // gesture, not a contact test — the floor is further down than an arm is long.
            _catchTicks++;
            if (_catchTicks < Profile.ScoopTicks)
                return false;
            _catchTicks = 0;
            _pendingLeftHandAttach = true;
            // Picking something up off the floor is never a clean catch.
            _catchWasClean = false;
            return true;
        }

        _catchTicks = 0;
        bool touched = CatchTouchedHand(tracked, out bool leftHand);
        if (touched)
        {
            _pendingLeftHandAttach = leftHand;
            // Judged at the instant of the catch: the player threw it, and it has not reached
            // the floor since. Both halves matter — a ball the buddy tossed itself carries no
            // throw token, and one that bounced first was not caught out of the air.
            _catchWasClean = known && live.ThrowToken != 0 && !live.TouchedGroundSinceThrow;
        }

        return touched;
    }

    private void HandleIntent(in ObjectIntent intent, Vector2 cursorWorldPosition)
    {
        LooseObjectBody? body = _registry.FindBody(intent.RuntimeId);
        switch (intent.Command)
        {
            case ObjectCommand.None:
                ApproachDirection = 0.0f;
                _catchTicks = 0;
                _throwWindupTicks = 0;
                _throwReleased = false;
                CurrentDriveCommand = ObjectDriveCommand.None;
                break;
            case ObjectCommand.Approach:
                ApproachDirection = intent.ApproachDirection;
                _catchTicks = 0;
                CurrentDriveCommand = ObjectDriveCommand.None;
                break;
            case ObjectCommand.Catch:
                ApproachDirection = 0.0f;
                CurrentDriveCommand = IsScooping
                    ? BuildScoopCommand(body)
                    : BuildCatchCommand(body);
                break;
            case ObjectCommand.Hold:
            case ObjectCommand.Inspect:
                ApproachDirection = 0.0f;
                if (!IsHolding && GodotObject.IsInstanceValid(body))
                    BeginHold(body!, _pendingLeftHandAttach);
                CurrentDriveCommand = BuildHoldCommand(_heldBody, ObjectDriveAction.Hold);
                if (intent.GrantsCatchCare)
                    GrantCatchReward();
                break;
            case ObjectCommand.Consume:
                ApproachDirection = 0.0f;
                if (!IsHolding && GodotObject.IsInstanceValid(body))
                    BeginHold(body!, _pendingLeftHandAttach);
                CurrentDriveCommand = BuildHoldCommand(_heldBody, ObjectDriveAction.Hold);
                if (intent.RequestsConsume)
                    TryBeginConsume();
                break;
            case ObjectCommand.Toss:
                ApproachDirection = 0.0f;
                // Wind up first, release on the forward beat: a slight hand motion is what
                // makes the return read as a throw (owner instruction 2026-07-26).
                if (_throwReleased)
                {
                    // Follow through on the free hand pose; the ball is already gone.
                    CurrentDriveCommand = ObjectDriveCommand.None;
                    break;
                }

                if (IsHolding)
                {
                    _throwWindupTicks++;
                    Vector2 throwDirection = ThrowDirection(_heldBody);
                    if (_throwWindupTicks <= Profile.ThrowWindupTicks)
                    {
                        // Beat one: draw the carrying hand back, away from the target.
                        CurrentDriveCommand = BuildCarryPoseCommand(
                            _heldBody,
                            ObjectDriveAction.ThrowWindup,
                            -throwDirection * Profile.ThrowWindupDistance);
                        break;
                    }

                    if (_throwWindupTicks <= Profile.ThrowWindupTicks + Profile.ThrowForwardTicks)
                    {
                        // Beat two: swing forward past the carry pose, object still in hand.
                        CurrentDriveCommand = BuildCarryPoseCommand(
                            _heldBody,
                            ObjectDriveAction.ThrowWindup,
                            throwDirection * Profile.ThrowForwardDistance);
                        break;
                    }
                }

                // Beat three: let go at the forward extent.
                _throwReleased = true;
                ReleaseWithImpulse(body, cursorWorldPosition, discard: false);
                break;
            case ObjectCommand.Discard:
                ApproachDirection = 0.0f;
                FleeBiasRequested = true;
                ReleaseWithImpulse(body, cursorWorldPosition, discard: true);
                break;
            case ObjectCommand.Drop:
                ApproachDirection = 0.0f;
                ReleaseHeld(ObjectDriveAction.Drop, Vector2.Zero);
                DropCount++;
                break;
        }

        if (intent.Abort != ObjectAbortReason.None && _consumeToken != 0)
            CancelConsume();
    }

    /// <summary>
    /// Pays out one once-per-throw catch. A catch off the floor is still a catch — it counts
    /// and it still earns care — but only one taken cleanly out of the air spends catch
    /// novelty, and only a buddy that still finds catch fun laughs about it.
    ///
    /// <para>Interest is what makes the tenth identical throw land flat: the drain is this
    /// buddy's own taste, so a buddy that loves catch keeps laughing far longer than one that
    /// does not (owner instruction 2026-07-27).</para>
    /// </summary>
    private void GrantCatchReward()
    {
        _progress.ApplyCareMood(1.0f);
        _progress.RecordSuccessfulCatch();
        CatchCareCount++;

        if (!_catchWasClean)
            return;

        CleanCatchCount++;
        FunOutcome outcome = _progress.EngageFun(FunActivityId.Catch);
        LastCatchInterest = outcome.InterestAfter;
        if (!outcome.WasFun)
            return;

        FunCatchCount++;
        FunCatchDelighted?.Invoke();
    }

    private void TryBeginConsume()
    {
        if (_consumeToken != 0 || !GodotObject.IsInstanceValid(_heldBody))
            return;

        string contentId = _heldBody!.SemanticContentId;
        ConsumeRejection rejection = ConsumeRejection.UnknownConsumable;
        if (contentId != ContentIds.CareLabFood ||
            !_consumables.TryBegin(contentId, out _consumeToken, out rejection))
        {
            LastConsumeRejection = contentId == ContentIds.CareLabFood
                ? rejection
                : ConsumeRejection.UnknownConsumable;
            _model.Reset();
            ReleaseHeld(ObjectDriveAction.Drop, Vector2.Zero);
            return;
        }

        LastConsumeRejection = ConsumeRejection.None;
        Activity.SetActivity(ActivityId.Eat);
        ConsumeStarted?.Invoke(_heldBody);
    }

    private void OnEatBiteCompleted(int completed, int total)
    {
        if (!IsInitialized || completed != total || _consumeToken == 0 ||
            !GodotObject.IsInstanceValid(_heldBody))
        {
            return;
        }

        ConsumeResult result = _consumables.Complete(_consumeToken, CareConsumableTuning.LabFood);
        _consumeToken = 0;
        if (!result.Applied)
            return;

        LooseObjectBody consumed = _heldBody!;
        _progress.ApplyCareMood(result.MoodGain);
        // A treat is one of the buddy's fun things too, on the same terms as catch: the mood
        // gain always lands, the novelty of it does not.
        _progress.EngageFun(FunActivityId.Treat);
        ConsumeSuccessCount++;
        _skipNextCooldownTick = true;
        _consumeCompleted = true;
        _directLabConsume = false;
        EndHold(consumed);
        _registry.Unregister(consumed);
        consumed.QueueFree();
        _heldBody = null;
        CurrentDriveCommand = ObjectDriveCommand.None;
        ConsumeSucceeded?.Invoke(consumed);
        Log.Info("ObjectInteraction",
            $"Lab food consume succeeded mood_gain={result.MoodGain:F0} " +
            $"cooldown_ticks={result.CooldownTicks}.");
    }

    private void OnActivityChanged(ActivityId activity)
    {
        if (!IsInitialized || activity == ActivityId.Eat || _consumeToken == 0)
            return;
        CancelConsume();
        _model.Reset();
        ReleaseHeld(ObjectDriveAction.Drop, Vector2.Zero);
    }

    private void CancelConsume()
    {
        if (_consumeToken == 0)
        {
            _directLabConsume = false;
            return;
        }

        _consumables.Cancel(_consumeToken);
        _consumeToken = 0;
        _directLabConsume = false;
        ConsumeCancelCount++;
        if (GodotObject.IsInstanceValid(_heldBody))
            ConsumeCancelled?.Invoke(_heldBody!);
    }

    /// <summary>
    /// Sticks the object to a hand. It is relocated onto the socket and frozen kinematic, so
    /// from here it rides the hand exactly instead of being sprung toward it — the owner's
    /// "the ball should stick to its hand" and "relocate directly to the buddy's hand".
    ///
    /// <para>This hard placement does not breach ARCHITECTURE §23: that invariant governs the
    /// buddy rig, whose bodies are still only ever driven by bounded forces. A carried object
    /// is cargo for as long as it is held, not a simulated participant.</para>
    /// </summary>
    /// <summary>
    /// Resolves this tick's collision exception, including the grace window that keeps a
    /// just-released object passing through the buddy for a moment so a thrown ball cannot
    /// clip the hand that threw it (owner correction 2026-07-27).
    /// </summary>
    private void ResolveCommitExceptions(LooseObjectBody? desired)
    {
        if (desired is not null)
        {
            _exceptionGraceTicks = 0;
            ApplyCommitExceptions(desired);
            return;
        }

        if (_exceptionGraceTicks > 0)
        {
            _exceptionGraceTicks--;
            return;
        }

        ApplyCommitExceptions(null);
    }

    /// <summary>
    /// Makes the committed object non-colliding with the buddy for as long as the buddy is
    /// going for it. Applied from <b>commitment</b>, not just from the hold: the feet reach a
    /// floor-resting ball at about `51 px`, so any approach kicked away the very object it was
    /// walking toward — the buddy shoved balls into a corner instead of picking them up.
    /// </summary>
    private void ApplyCommitExceptions(LooseObjectBody? body)
    {
        if (_exceptionBody == body)
            return;

        if (GodotObject.IsInstanceValid(_exceptionBody))
        {
            for (int index = 0; index < Rig.Parts.Count; index++)
                _exceptionBody!.RemoveCollisionExceptionWith(Rig.Parts[index]);
        }

        _exceptionBody = GodotObject.IsInstanceValid(body) ? body : null;
        if (_exceptionBody is not null)
        {
            for (int index = 0; index < Rig.Parts.Count; index++)
                _exceptionBody.AddCollisionExceptionWith(Rig.Parts[index]);
        }

        _collisionExceptionsActive = _exceptionBody is not null;
    }

    private void BeginHold(LooseObjectBody body, bool leftHand)
    {
        _heldBody = body;
        _attachedToLeftHand = leftHand;
        _registry.SetBuddyHeld(body, true);
        ApplyCommitExceptions(body);

        _heldLinearDamp = body.LinearDamp;
        _heldAngularDamp = body.AngularDamp;
        body.LinearVelocity = Vector2.Zero;
        body.AngularVelocity = 0.0f;
        body.Sleeping = false;
        body.FreezeMode = RigidBody2D.FreezeModeEnum.Kinematic;
        body.Freeze = true;
        IsAttached = true;
        FollowAttachedHand();
    }

    private void EndHold(LooseObjectBody body)
    {
        _registry.SetBuddyHeld(body, false);
        // Keep it passing through the buddy for a moment rather than clearing immediately.
        _exceptionGraceTicks = Profile.ReleaseCollisionGraceTicks;
        if (IsAttached)
        {
            body.Freeze = false;
            body.FreezeMode = RigidBody2D.FreezeModeEnum.Static;
            body.LinearDamp = _heldLinearDamp;
            body.AngularDamp = _heldAngularDamp;
            // Hand it the carrying hand's own motion so a release continues the gesture
            // instead of dropping the object dead in the air.
            body.LinearVelocity = AttachedHand.LinearVelocity;
            body.ResetPhysicsInterpolation();
            IsAttached = false;
        }
    }

    private PuppetPartBody AttachedHand => _attachedToLeftHand ? Rig.LeftHand : Rig.RightHand;

    /// <summary>Sign of the side the carrying hand lives on.</summary>
    private float CarrySide => _attachedToLeftHand ? -1.0f : 1.0f;

    /// <summary>
    /// The carrying hand's pose: its natural resting side, so the buddy just stands there
    /// holding the object rather than clutching it to its chest. Eating overrides this through
    /// the activity hand reach, which takes the object to the mouth.
    /// </summary>
    private Vector2 CarryHandTarget() =>
        Rig.Torso.GlobalPosition +
        new Vector2(CarrySide * Profile.CarryHandOffset.X, Profile.CarryHandOffset.Y);

    /// <summary>The free hand mirrors the carry pose so it is never yanked across the body.</summary>
    private Vector2 FreeHandTarget() =>
        Rig.Torso.GlobalPosition +
        new Vector2(-CarrySide * Profile.CarryHandOffset.X, Profile.CarryHandOffset.Y);

    /// <summary>
    /// Where a carried object sits: resting <b>on top of the carrying hand</b>. Carrying it at
    /// the midpoint between both hands put it inside the torso, and lifting it from there put
    /// it inside the head — on this rig the head's underside (`-26`) and the torso's top
    /// (`-28`) leave no gap, so the only clear space is out to the side.
    /// </summary>
    private Vector2 CarrySocket()
    {
        PuppetPartBody hand = AttachedHand;
        float lift = _heldBody is null
            ? 0.0f
            : hand.Radius + _heldBody.Radius - Profile.CatchHandClearance;
        return hand.GlobalPosition + new Vector2(0.0f, -lift * Profile.CarryLiftFraction);
    }

    /// <summary>
    /// Re-states a launch velocity for a few ticks after release. The object resumes simulation
    /// on the release tick, and a single velocity assignment against a body that has just left
    /// its frozen state can be overwritten before it ever integrates — which is why the throw
    /// looked like a drop. Re-stating it makes the launch deterministic instead of a race.
    /// </summary>
    private void HoldLaunchVelocity()
    {
        if (_launchTicksRemaining <= 0)
            return;
        _launchTicksRemaining--;
        if (!GodotObject.IsInstanceValid(_launchBody))
        {
            _launchBody = null;
            _launchTicksRemaining = 0;
            return;
        }

        _launchBody!.LinearVelocity = _launchVelocity;
        if (_launchTicksRemaining <= 0)
            _launchBody = null;
    }

    /// <summary>Keeps an attached object riding on top of its carrying hand this tick.</summary>
    private void FollowAttachedHand()
    {
        if (!IsAttached || !GodotObject.IsInstanceValid(_heldBody))
            return;
        _heldBody!.GlobalPosition = CarrySocket();
        _heldBody.LinearVelocity = Vector2.Zero;
        _heldBody.AngularVelocity = 0.0f;
    }

    private void ReleaseHeld(ObjectDriveAction action, Vector2 impulse)
    {
        LooseObjectBody? body = _heldBody;
        if (!GodotObject.IsInstanceValid(body))
        {
            _heldBody = null;
            _collisionExceptionsActive = false;
            CurrentDriveCommand = ObjectDriveCommand.None;
            return;
        }

        EndHold(body!);
        _registry.MarkBuddyReleased(body!, Profile.ReleaseIgnoreTicks);
        CurrentDriveCommand = BuildReleaseCommand(body!, action, impulse);
        LastReleaseImpulse = impulse;
        if (impulse != Vector2.Zero)
        {
            _launchBody = body;
            _launchVelocity = impulse;
            _launchTicksRemaining = Profile.LaunchHoldTicks;
        }
        _heldBody = null;
    }

    private void ReleaseWithImpulse(
        LooseObjectBody? candidate,
        Vector2 cursorWorldPosition,
        bool discard)
    {
        LooseObjectBody? body = GodotObject.IsInstanceValid(_heldBody) ? _heldBody : candidate;
        if (!GodotObject.IsInstanceValid(body))
        {
            CurrentDriveCommand = ObjectDriveCommand.None;
            return;
        }

        // A toss now returns the ball *to* the player, reversing the earlier cursor-safe
        // away-from-cursor policy (owner instruction 2026-07-26). Discard keeps the low
        // energy away-release, because its whole point is getting rid of something.
        Vector2 impulse;
        if (discard)
        {
            float away = Mathf.Sign(Rig.Torso.GlobalPosition.X - cursorWorldPosition.X);
            if (Mathf.IsZeroApprox(away))
                away = 1.0f;
            impulse = new Vector2(away * Profile.DiscardSpeed, -Profile.DiscardLiftSpeed);
        }
        else
        {
            // The hand has already swung forward; let go from where it actually is, plus the
            // object's own clearance, so the ball leaves the hand rather than the body.
            (Vector2 release, Vector2 velocity) = SolveThrow(body);
            impulse = velocity;
            LastReleaseOrigin = release;
            if (IsAttached)
                body!.GlobalPosition = release;
        }

        ReleaseHeld(
            discard ? ObjectDriveAction.Discard : ObjectDriveAction.Toss,
            impulse);
        if (discard)
            DiscardCount++;
        else
            TossCount++;
    }

    /// <summary>
    /// Clamps any desired hand position into the reach envelope. This is the single place
    /// that guarantees the arms never stretch further than a minimal extension past their
    /// natural length, however far away the object is.
    /// </summary>
    private Vector2 ClampToReach(Vector2 desired)
    {
        Vector2 origin = ReachOrigin;
        Vector2 offset = desired - origin;
        float length = offset.Length();
        float limit = Profile.MaximumReach;
        Vector2 result = length <= limit || Mathf.IsZeroApprox(length)
            ? desired
            : origin + (offset / length * limit);
        MaximumCommandedReach = Mathf.Max(MaximumCommandedReach, result.DistanceTo(origin));
        return result;
    }

    /// <summary>
    /// Reach toward an incoming object, clamped to arm's length. The object is never pulled
    /// in; the catch happens when it arrives and touches a hand.
    /// </summary>
    private ObjectDriveCommand BuildCatchCommand(LooseObjectBody? body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return ObjectDriveCommand.None;
        float half = body!.Radius + Profile.CatchHandClearance;
        Vector2 separation = new(half, 0.0f);
        return BuildHandCommand(
            ObjectDriveAction.Catch,
            body,
            body.GlobalPosition - separation,
            body.GlobalPosition + separation);
    }

    /// <summary>Lower the hands onto a resting object and dip the body slightly.</summary>
    private ObjectDriveCommand BuildScoopCommand(LooseObjectBody? body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return ObjectDriveCommand.None;
        float half = body!.Radius + Profile.CatchHandClearance;
        Vector2 separation = new(half, 0.0f);
        return BuildHandCommand(
            ObjectDriveAction.Scoop,
            body,
            body.GlobalPosition - separation,
            body.GlobalPosition + separation,
            Profile.ScoopDipForce);
    }

    /// <summary>Carry pose: the holding hand out to its own side, the free hand mirrored.</summary>
    private ObjectDriveCommand BuildHoldCommand(LooseObjectBody? body, ObjectDriveAction action) =>
        BuildCarryPoseCommand(body, action, Vector2.Zero);

    /// <summary>
    /// One beat of the throw. The carrying hand draws back along <paramref name="handShift"/>
    /// and then swings forward past the carry pose; the object rides it, so the release reads
    /// as a throw instead of the ball simply teleporting out of the body.
    /// </summary>
    private ObjectDriveCommand BuildCarryPoseCommand(
        LooseObjectBody? body,
        ObjectDriveAction action,
        Vector2 handShift)
    {
        if (!GodotObject.IsInstanceValid(body))
            return ObjectDriveCommand.None;
        Vector2 carry = CarryHandTarget() + handShift;
        Vector2 free = FreeHandTarget();
        return BuildHandCommand(
            action,
            body!,
            _attachedToLeftHand ? carry : free,
            _attachedToLeftHand ? free : carry);
    }

    private ObjectDriveCommand BuildReleaseCommand(
        LooseObjectBody body,
        ObjectDriveAction action,
        Vector2 impulse) =>
        new(
            action,
            body,
            Vector2.Zero,
            Vector2.Zero,
            impulse,
            Profile.HandStiffness,
            Profile.HandDamping,
            Profile.MaximumHandForce);

    private ObjectDriveCommand BuildHandCommand(
        ObjectDriveAction action,
        LooseObjectBody body,
        Vector2 left,
        Vector2 right,
        float dipForce = 0.0f) =>
        new(
            action,
            body,
            ClampToReach(left),
            ClampToReach(right),
            Vector2.Zero,
            Profile.HandStiffness,
            Profile.HandDamping,
            // The throw gets its own force budget: the gentle carry force cannot swing an arm
            // in a handful of ticks, so the wind-up barely moved and the release read as a drop.
            action == ObjectDriveAction.ThrowWindup
                ? Profile.ThrowHandForce
                : Profile.MaximumHandForce,
            dipForce);

    /// <summary>Where the throw leaves from — the carrying hand once the object is attached.</summary>
    private Vector2 ThrowOrigin => IsAttached
        ? AttachedHand.GlobalPosition
        : Rig.Torso.GlobalPosition + Profile.HoldCenterOffset;

    /// <summary>
    /// The launch velocity from <paramref name="origin"/> that lands the object on the player's
    /// cursor (owner instruction 2026-07-27). <see cref="ThrowArc"/> solves it from a fixed
    /// flight time against the live gravity and damping of the body being thrown, so the ball
    /// arrives at the cursor rather than merely heading its way — and always leaves along an
    /// arc, because the upward component carries the whole fall.
    ///
    /// <para>If the body is gone or the solve degenerates, this falls back to the old flat
    /// horizontal launch plus a fixed lift, so a throw always happens.</para>
    /// </summary>
    private Vector2 SolveLaunchFrom(Vector2 origin, LooseObjectBody? body)
    {
        Vector2 displacement = _cursorWorldPosition - origin;
        if (GodotObject.IsInstanceValid(body))
        {
            ThrowArcResult solved = ThrowArc.Solve(
                new NumericsVector2(displacement.X, displacement.Y),
                _defaultGravity * body!.GravityScale,
                body.LinearDamp,
                Profile.ThrowFlightSeconds,
                Profile.TossSpeed);
            if (solved.IsValid)
                return new Vector2(solved.Velocity.X, solved.Velocity.Y);
        }

        float side = Mathf.IsZeroApprox(displacement.X) ? 1.0f : Mathf.Sign(displacement.X);
        return new Vector2(side * Profile.TossSpeed, -Profile.TossLiftSpeed);
    }

    /// <summary>
    /// Where the ball leaves from and how fast. The object is let go a little way along the
    /// throwing line so it clears the hand, which means the hand is <i>not</i> where the flight
    /// begins — solving from the hand overshot the cursor by exactly that clearance. So the
    /// first pass only establishes the line, and the arc is then solved again from the real
    /// release point.
    /// </summary>
    private (Vector2 Position, Vector2 Velocity) SolveThrow(LooseObjectBody? body)
    {
        Vector2 handOrigin = ThrowOrigin;
        if (!IsAttached || !GodotObject.IsInstanceValid(body))
            return (handOrigin, SolveLaunchFrom(handOrigin, body));

        Vector2 line = SolveLaunchFrom(handOrigin, body);
        Vector2 direction = line.LengthSquared() < 1.0f ? Vector2.Right : line.Normalized();
        Vector2 release = handOrigin + (direction * (AttachedHand.Radius + body!.Radius));
        return (release, SolveLaunchFrom(release, body));
    }

    /// <summary>
    /// Unit direction of the throw, so the wind-up draws back along the same line the ball will
    /// actually leave on and the release clears the hand in the right direction.
    /// </summary>
    private Vector2 ThrowDirection(LooseObjectBody? body)
    {
        Vector2 velocity = SolveThrow(body).Velocity;
        return velocity.LengthSquared() < 1.0f ? Vector2.Right : velocity.Normalized();
    }

    /// <summary>An attached object cannot drift, so the grip only breaks if it detaches.</summary>
    private bool HoldStillIntact() => GodotObject.IsInstanceValid(_heldBody) && IsAttached;

    /// <summary>
    /// The catch confirms when the object arrives in the buddy's hands. Two ways, because a
    /// thrown ball flies at the body and the torso is wider than the gap between the hands:
    /// either it touches a hand, or it enters the reach envelope at all — which is outside
    /// the torso surface, so the catch lands just before the ball would bounce off the belly.
    /// Requiring a hand-centre hit alone meant thrown balls simply rebounded, which is why
    /// the buddy appeared not to react to them.
    /// </summary>
    private bool CatchTouchedHand(LooseObjectBody? body, out bool leftHand)
    {
        leftHand = false;
        if (!GodotObject.IsInstanceValid(body))
            return false;

        float toLeft = Rig.LeftHand.GlobalPosition.DistanceTo(body!.GlobalPosition);
        float toRight = Rig.RightHand.GlobalPosition.DistanceTo(body.GlobalPosition);
        leftHand = toLeft <= toRight;

        float slack = body.Radius + Profile.CatchContactTolerance;
        if (toLeft <= Rig.LeftHand.Radius + slack || toRight <= Rig.RightHand.Radius + slack)
            return true;

        return ReachOrigin.DistanceTo(body.GlobalPosition) <=
            Profile.MaximumReach + body.Radius;
    }

    private void OnBodyEntered(Node2D node)
    {
        if (node is not LooseObjectBody body || body.RuntimeId == 0)
            return;

        // Two passes on purpose: a single pass that inserts at the first free slot can
        // pass over an existing entry at a higher index and admit the same body twice,
        // which would double-score it in the candidate buffer.
        int free = -1;
        for (int index = 0; index < _sensed.Length; index++)
        {
            LooseObjectBody? sensed = _sensed[index];
            if (sensed == body)
                return;
            if (free < 0 && (sensed is null || !GodotObject.IsInstanceValid(sensed)))
            {
                free = index;
                if (sensed is not null)
                {
                    // A freed body that never raised BodyExited: reclaim its accounting.
                    _sensed[index] = null;
                    SensedCount = Mathf.Max(0, SensedCount - 1);
                }
            }
        }

        if (free < 0)
            return;
        _sensed[free] = body;
        SensedCount++;
    }

    private void OnBodyExited(Node2D node)
    {
        if (node is not LooseObjectBody body)
            return;
        for (int index = 0; index < _sensed.Length; index++)
        {
            if (_sensed[index] != body)
                continue;
            _sensed[index] = null;
            SensedCount = Mathf.Max(0, SensedCount - 1);
            return;
        }
    }

    private void TickCooldowns()
    {
        if (_skipNextCooldownTick)
        {
            _skipNextCooldownTick = false;
            return;
        }
        _consumables.Tick();
    }

    public override void _ExitTree()
    {
        if (!IsInitialized)
            return;
        BodyEntered -= OnBodyEntered;
        BodyExited -= OnBodyExited;
        if (GodotObject.IsInstanceValid(Activity))
        {
            Activity.EatBiteCompleted -= OnEatBiteCompleted;
            Activity.ActivityChanged -= OnActivityChanged;
        }
    }
}

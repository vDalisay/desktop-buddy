using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Objects;
using Godot;

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
    private bool _pendingLeftHandAttach;
    private bool _attachedToLeftHand;
    private LooseObjectBody? _exceptionBody;
    private float _heldLinearDamp;
    private float _heldAngularDamp;
    private Vector2 _cursorWorldPosition;

    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public BehaviorActivityComponent Activity { get; set; } = null!;
    [Export] public ObjectInteractionProfile Profile { get; set; } = null!;

    public event Action<LooseObjectBody>? ConsumeStarted;
    public event Action<LooseObjectBody>? ConsumeCancelled;
    public event Action<LooseObjectBody>? ConsumeSucceeded;

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
    public int TossCount { get; private set; }
    public int DiscardCount { get; private set; }
    public int DropCount { get; private set; }
    public int ConsumeSuccessCount { get; private set; }
    public int ConsumeCancelCount { get; private set; }
    public ConsumeRejection LastConsumeRejection { get; private set; }
    public ObjectDriveCommand CurrentDriveCommand { get; private set; }
    public Vector2 LastReleaseImpulse { get; private set; }

    /// <summary>True while the held object is attached to a hand socket.</summary>
    public bool IsAttached { get; private set; }

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
        ApplyCommitExceptions(committed);

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
        if (_registry.TryGetSnapshot(tracked!.RuntimeId, out LooseObjectSnapshot live) &&
            live.PlayerHeld)
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
            return true;
        }

        _catchTicks = 0;
        bool touched = CatchTouchedHand(tracked, out bool leftHand);
        if (touched)
            _pendingLeftHandAttach = leftHand;
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
                {
                    _progress.ApplyCareMood(1.0f);
                    _progress.RecordSuccessfulCatch();
                    CatchCareCount++;
                }
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
                    CurrentDriveCommand = ObjectDriveCommand.None;
                    break;
                }
                if (_throwWindupTicks < Profile.ThrowWindupTicks && IsHolding)
                {
                    _throwWindupTicks++;
                    CurrentDriveCommand = BuildThrowWindupCommand(_heldBody);
                    break;
                }
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
        ApplyCommitExceptions(null);
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

    /// <summary>
    /// Where a carried object sits: centred <b>between both hands</b>, not pinned to one of
    /// them. Pinning it to the hand that happened to make contact put the Eat item off to one
    /// side instead of in front of the mouth (owner correction 2026-07-26).
    /// </summary>
    private Vector2 CarrySocket()
    {
        Vector2 midpoint =
            (Rig.LeftHand.GlobalPosition + Rig.RightHand.GlobalPosition) * 0.5f;
        float lift = _heldBody is null
            ? 0.0f
            : Rig.LeftHand.Radius + _heldBody.Radius - Profile.CatchHandClearance;
        return midpoint + new Vector2(0.0f, -lift * Profile.CarryLiftFraction);
    }

    /// <summary>Keeps an attached object glued to the two-hand carry socket this tick.</summary>
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
            impulse = new Vector2(away * Profile.DiscardImpulse, -Profile.DiscardLiftImpulse);
        }
        else
        {
            Vector2 toward = ThrowDirection();
            impulse = (toward * Profile.TossImpulse) +
                new Vector2(0.0f, -Profile.TossLiftImpulse);
            // Leave the hand from in front of the body. Releasing at the carry pose fired the
            // ball through the buddy's own head, where it wedged between head and torso.
            if (IsAttached && GodotObject.IsInstanceValid(body))
            {
                Vector2 hands =
                    (Rig.LeftHand.GlobalPosition + Rig.RightHand.GlobalPosition) * 0.5f;
                body!.GlobalPosition = hands + (toward * Profile.ThrowReleaseForward);
            }
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

    private ObjectDriveCommand BuildHoldCommand(LooseObjectBody? body, ObjectDriveAction action)
    {
        if (!GodotObject.IsInstanceValid(body))
            return ObjectDriveCommand.None;
        Vector2 center = Rig.Torso.GlobalPosition + Profile.HoldCenterOffset;
        Vector2 separation = new(Profile.HoldHandHalfSeparation, 0.0f);
        return BuildHandCommand(action, body!, center - separation, center + separation);
    }

    /// <summary>
    /// The wind-up beat of the return throw: the hands draw back away from the cursor so the
    /// release that follows reads as a throw rather than a drop.
    /// </summary>
    private ObjectDriveCommand BuildThrowWindupCommand(LooseObjectBody? body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return ObjectDriveCommand.None;
        Vector2 center = Rig.Torso.GlobalPosition + Profile.HoldCenterOffset;
        Vector2 back = ThrowDirection() * -Profile.HoldHandHalfSeparation;
        Vector2 separation = new(Profile.HoldHandHalfSeparation, 0.0f);
        return BuildHandCommand(
            ObjectDriveAction.ThrowWindup,
            body!,
            center + back - separation,
            center + back + separation);
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
            Profile.MaximumHandForce,
            dipForce);

    /// <summary>Unit direction from the carry pose toward the player's cursor.</summary>
    private Vector2 ThrowDirection()
    {
        Vector2 toCursor = _cursorWorldPosition -
            (Rig.Torso.GlobalPosition + Profile.HoldCenterOffset);
        return toCursor.LengthSquared() < 1.0f ? Vector2.Right : toCursor.Normalized();
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

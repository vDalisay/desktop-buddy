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
        bool holdConfirmed = IsHolding
            ? HoldStillIntact()
            : CatchHandsReady(_registry.FindBody(_model.TrackedRuntimeId));

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
        TickCooldowns();
    }

    /// <summary>
    /// Debug-laboratory compatibility path for E: the key now opens the real
    /// care transaction on a registered lab-food body and succeeds only on bite five.
    /// </summary>
    public bool TryBeginLaboratoryFoodConsume(LooseObjectBody body)
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(body) ||
            body.SemanticContentId != ContentIds.CareLabFood || IsHolding ||
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
        BeginHold(body);
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
    }

    private int BuildCandidates()
    {
        int count = 0;
        float torsoX = Rig.Torso.GlobalPosition.X;
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
            if (!_registry.TryGetSnapshot(body.RuntimeId, out LooseObjectSnapshot snapshot) ||
                snapshot.PlayerHeld || snapshot.BuddyHeld)
            {
                continue;
            }

            float offsetX = body.GlobalPosition.X - torsoX;
            _candidates[count++] = new ObjectCandidate(
                snapshot.RuntimeId,
                snapshot.ContentId,
                snapshot.ThrowToken,
                Mathf.Abs(offsetX),
                Mathf.IsZeroApprox(offsetX) ? 1.0f : Mathf.Sign(offsetX),
                snapshot.Consumable,
                snapshot.AtRest);
        }
        return count;
    }

    private void HandleIntent(in ObjectIntent intent, Vector2 cursorWorldPosition)
    {
        LooseObjectBody? body = _registry.FindBody(intent.RuntimeId);
        switch (intent.Command)
        {
            case ObjectCommand.None:
                ApproachDirection = 0.0f;
                CurrentDriveCommand = ObjectDriveCommand.None;
                break;
            case ObjectCommand.Approach:
                ApproachDirection = intent.ApproachDirection;
                CurrentDriveCommand = ObjectDriveCommand.None;
                break;
            case ObjectCommand.Catch:
                ApproachDirection = 0.0f;
                CurrentDriveCommand = BuildCatchCommand(body);
                break;
            case ObjectCommand.Hold:
            case ObjectCommand.Inspect:
                ApproachDirection = 0.0f;
                if (!IsHolding && GodotObject.IsInstanceValid(body))
                    BeginHold(body!);
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
                    BeginHold(body!);
                CurrentDriveCommand = BuildHoldCommand(_heldBody, ObjectDriveAction.Hold);
                if (intent.RequestsConsume)
                    TryBeginConsume();
                break;
            case ObjectCommand.Toss:
                ApproachDirection = 0.0f;
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

    private void BeginHold(LooseObjectBody body)
    {
        _heldBody = body;
        _registry.SetBuddyHeld(body, true);
        for (int index = 0; index < Rig.Parts.Count; index++)
            body.AddCollisionExceptionWith(Rig.Parts[index]);
        _collisionExceptionsActive = true;
    }

    private void EndHold(LooseObjectBody body)
    {
        _registry.SetBuddyHeld(body, false);
        for (int index = 0; index < Rig.Parts.Count; index++)
            body.RemoveCollisionExceptionWith(Rig.Parts[index]);
        _collisionExceptionsActive = false;
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
        _registry.MarkBuddyReleased(body!);
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

        float away = Mathf.Sign(Rig.Torso.GlobalPosition.X - cursorWorldPosition.X);
        if (Mathf.IsZeroApprox(away))
            away = 1.0f;
        Vector2 impulse = discard
            ? new Vector2(away * Profile.DiscardImpulse, -Profile.DiscardLiftImpulse)
            : new Vector2(away * Profile.TossImpulse, -Profile.TossLiftImpulse);
        ReleaseHeld(
            discard ? ObjectDriveAction.Discard : ObjectDriveAction.Toss,
            impulse);
        if (discard)
            DiscardCount++;
        else
            TossCount++;
    }

    private ObjectDriveCommand BuildCatchCommand(LooseObjectBody? body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return ObjectDriveCommand.None;
        float half = body!.Radius + Profile.CatchHandClearance;
        Vector2 separation = new(half, 0.0f);
        return BuildDriveCommand(
            ObjectDriveAction.Catch,
            body,
            body.GlobalPosition - separation,
            body.GlobalPosition + separation,
            body.GlobalPosition);
    }

    private ObjectDriveCommand BuildHoldCommand(LooseObjectBody? body, ObjectDriveAction action)
    {
        if (!GodotObject.IsInstanceValid(body))
            return ObjectDriveCommand.None;
        Vector2 center = Rig.Torso.GlobalPosition + Profile.HoldCenterOffset;
        Vector2 separation = new(Profile.HoldHandHalfSeparation, 0.0f);
        return BuildDriveCommand(
            action,
            body!,
            center - separation,
            center + separation,
            center);
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
            Vector2.Zero,
            impulse,
            Profile.HandStiffness,
            Profile.HandDamping,
            Profile.MaximumHandForce,
            Profile.ObjectStiffness,
            Profile.ObjectDamping,
            Profile.MaximumObjectForce);

    private ObjectDriveCommand BuildDriveCommand(
        ObjectDriveAction action,
        LooseObjectBody body,
        Vector2 left,
        Vector2 right,
        Vector2 target) =>
        new(
            action,
            body,
            left,
            right,
            target,
            Vector2.Zero,
            Profile.HandStiffness,
            Profile.HandDamping,
            Profile.MaximumHandForce,
            Profile.ObjectStiffness,
            Profile.ObjectDamping,
            Profile.MaximumObjectForce);

    /// <summary>
    /// Physical hold feedback. The bounded hold force can lose an object to a hard enough
    /// disturbance; when it does, the model takes its <c>Drop</c> path and any open consume
    /// token is cancelled without starting a cooldown (FR-008.10).
    /// </summary>
    private bool HoldStillIntact()
    {
        if (!GodotObject.IsInstanceValid(_heldBody))
            return false;
        Vector2 center = Rig.Torso.GlobalPosition + Profile.HoldCenterOffset;
        return center.DistanceTo(_heldBody!.GlobalPosition) <= Profile.HoldReleaseDistance;
    }

    private bool CatchHandsReady(LooseObjectBody? body)
    {
        if (!GodotObject.IsInstanceValid(body))
            return false;
        float half = body!.Radius + Profile.CatchHandClearance;
        Vector2 separation = new(half, 0.0f);
        return Rig.LeftHand.GlobalPosition.DistanceTo(body.GlobalPosition - separation) <=
                Profile.CatchConfirmDistance &&
            Rig.RightHand.GlobalPosition.DistanceTo(body.GlobalPosition + separation) <=
                Profile.CatchConfirmDistance;
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

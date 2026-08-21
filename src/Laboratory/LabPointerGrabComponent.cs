using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>
/// Development-only pointer harness for the physics laboratory. It maps the mouse
/// onto the real <see cref="GrabTetherController"/> contract so a developer can
/// grab, drag, and throw the buddy or a loose object by hand while tuning — the
/// same TryGrab/MoveCursor/Release path the Milestone 2 input layer will drive.
///
/// It is inert outside debug builds: release/exported builds exclude the lab scene.
/// Headless activation is intentional because journeys synthesize events through
/// Godot's real input queue. Picking a body
/// under the cursor is an input concern and stays here; the tether keeps its pure
/// "acquire any body through one contract" role. The owning <see cref="App.BuddyLab"/>
/// calls <see cref="ResolvePendingInput"/> from its single fixed tick, where the
/// physics space state is valid.
/// </summary>
[GlobalClass]
public partial class LabPointerGrabComponent : Node2D
{
    private const float PickRadius = 10.0f;
    private const int MaxPickResults = 8;
    private const uint PickMask = CollisionLayers.BuddyParts | CollisionLayers.LooseObjects;
    private static readonly Color TetherColor = new("ff6b6b");

    [Export] public GrabTetherController Grab { get; set; } = null!;

    /// <summary>
    /// Tuning for the purchased Power Grab. Null leaves every grab Normal, which is what a
    /// scene that has not wired the resource gets — not a crash and not a stronger grab.
    /// </summary>
    [Export] public PowerGrabProfile? PowerProfile { get; set; }

    // Optional tool routing (buddy lab only; the dual-profile lab wires none of
    // these and keeps the grab-only behavior). When present, the pointer feeds
    // the selected tool instead of always grabbing: care strokes and the glove
    // cursor follow real pointer input only, so headless scenarios that drive
    // the tool APIs directly are never clobbered by a stale (0,0) cursor.
    [Export] public InteractionDamageComponent? Pipeline { get; set; }
    [Export] public CursorToolController? CursorTools { get; set; }
    [Export] public CareStrokeComponent? CareTool { get; set; }
    [Export] public ToolCursorPresenter? CareCursor { get; set; }
    [Export] public CareToolVisual3D? CareCursorVisual { get; set; }
    [Export] public PullbackLauncherComponent? LauncherTool { get; set; }
    [Export] public GrenadeComponent? GrenadeTool { get; set; }
    [Export] public CursorGunComponent? GunTool { get; set; }
    [Export] public FireSprayerComponent? SprayerTool { get; set; }

    private bool _active;
    private bool _pendingPress;
    private bool _pendingRelease;
    private bool _pendingSecondaryPress;
    private bool _pendingSecondaryRelease;
    private string? _pendingLaunchableSpawn;
    private bool _pendingReload;
    private int _pendingWheelSteps;
    private bool _ownsGrab;
    private bool _sawPointerInput;
    private Vector2 _cursor;

    public Func<RigidBody2D, bool>? PickFilter { get; set; }

    public bool IsActive => _active;
    public bool HasPointerInput => _sawPointerInput;
    public bool IsPrimaryHeld { get; private set; }
    public bool IsSecondaryHeld { get; private set; }
    public Vector2 WorldCursor { get; private set; }
    public int ReceivedInputCount { get; private set; }
    public BuddyPartId? LastPickedPart { get; private set; }
    public int SuccessfulPickCount { get; private set; }

    public void Initialize(bool developmentOnly = true)
    {
        if (!GodotObject.IsInstanceValid(Grab))
        {
            throw new InvalidOperationException("LabPointerGrabComponent requires an injected GrabTetherController.");
        }

        // Development affordance only. Headless journeys use the same queued-input path.
        _active = !developmentOnly || BuildInfo.IsDebugBuild;
        SetProcessInput(_active);
        SetProcessUnhandledInput(_active);
    }

    public override void _Input(InputEvent @event)
    {
        // Track all mouse motion so GUI-consumed events still keep the cursor truthful;
        // gameplay actions are handled only after GUI has declined the event.
        if (_active && @event is InputEventMouse mouse)
        {
            _cursor = mouse.Position;
            _sawPointerInput = true;
        }
    }

    public override void _Notification(int what)
    {
        if (_active && what == NotificationWMMouseExit)
        {
            NotifyPointerExitedPlayArea();
        }
    }

    /// <summary>
    /// Ends cursor-owned presentation/physics when Windows reports that the
    /// pointer left the client play area. Selection remains unchanged, so the
    /// same tool resumes only after a fresh in-window pointer event.
    /// </summary>
    public void NotifyPointerExitedPlayArea()
    {
        _sawPointerInput = false;
        IsPrimaryHeld = false;
        IsSecondaryHeld = false;
        _pendingPress = false;
        _pendingRelease = false;
        _pendingSecondaryPress = false;
        _pendingSecondaryRelease = false;
        _pendingLaunchableSpawn = null;
        _pendingReload = false;
        _pendingWheelSteps = 0;

        if (GunTool is not null && GodotObject.IsInstanceValid(GunTool))
        {
            // The trigger goes with the pointer. Shots already in flight are the
            // room's and keep travelling.
            GunTool.ClearCursor();
        }

        if (SprayerTool is not null && GodotObject.IsInstanceValid(SprayerTool))
        {
            // Same rule as the trigger: the stream stops with the pointer, and droplets
            // already in the air are the room's and keep travelling.
            SprayerTool.ClearCursor();
        }

        if (CursorTools is not null && GodotObject.IsInstanceValid(CursorTools))
        {
            // Grip and charge go with the pointer; the despawn inside ClearCursor
            // would abandon them anyway, but a tool that is still selected must
            // not resume holding a button nobody is pressing.
            CursorTools.SetGrip(false);
            CursorTools.SetChargeHeld(false);
            CursorTools.ClearCursor();
        }
        if (CareTool is not null && GodotObject.IsInstanceValid(CareTool))
            CareTool.SetStroke(false, WorldCursor);
        {
            ToolId tool = Pipeline is not null && GodotObject.IsInstanceValid(Pipeline)
                ? Pipeline.SelectedTool
                : ToolId.Grab;
            if (CareCursorVisual is not null && GodotObject.IsInstanceValid(CareCursorVisual))
                CareCursorVisual.SetPointerState(tool, WorldCursor, false);
            if (CareCursor is not null && GodotObject.IsInstanceValid(CareCursor))
                CareCursor.SetPointerState(tool, WorldCursor, false);
        }
        if (LauncherTool is not null && GodotObject.IsInstanceValid(LauncherTool))
            LauncherTool.RequestCancel();
    }

    /// <summary>
    /// Whether the care-tool cursor is drawn. The feather rides the pointer the whole time it is
    /// equipped, like every other tool the player holds; the brush is still a gesture the player
    /// makes with the button down (owner instruction 2026-08-19).
    /// </summary>
    private bool CareToolShown(ToolId tool) => tool switch
    {
        ToolId.Tickle => true,
        ToolId.Pet => _sawPointerInput && IsPrimaryHeld,
        _ => false,
    };

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_active)
            return;

        ReceivedInputCount++;

        if (@event.IsActionPressed(InputActions.Primary))
        {
            _pendingPress = true;
            IsPrimaryHeld = true;
            _sawPointerInput = true;
        }
        else if (@event.IsActionReleased(InputActions.Primary))
        {
            _pendingRelease = true;
            IsPrimaryHeld = false;
        IsSecondaryHeld = false;
        }
        else if (@event.IsActionPressed(InputActions.Secondary))
        {
            _pendingSecondaryPress = true;
            IsSecondaryHeld = true;
        }
        else if (@event.IsActionReleased(InputActions.Secondary))
        {
            _pendingSecondaryRelease = true;
            IsSecondaryHeld = false;
        }
        else if (@event.IsActionPressed(InputActions.Reload))
        {
            // The existing buddy_reload action, routed through the same queued-input
            // path as every other tool intent — never a direct key read in the gun.
            _pendingReload = true;
        }
        else if (@event is InputEventMouseButton
                 {
                     Pressed: true,
                     ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown,
                 } wheel)
        {
            _pendingWheelSteps += wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
        }
        else if (LabDevKeys.Enabled &&
                 Pipeline is not null && GodotObject.IsInstanceValid(Pipeline) &&
                 @event is InputEventKey { Pressed: true, Echo: false } key)
        {
            // Legacy launchable hotkeys remain for the non-grenade debug content. Grenade moved
            // to its selected-tool mouse interaction: RMB places one, LMB picks it up, RMB while
            // held starts the established pullback/pin chord.
            string? launchable = key.PhysicalKeycode switch
            {
                Key.Key5 => ContentIds.ToolBaseball,
                Key.Key6 => ContentIds.ToolMeal,
                Key.Key8 => ContentIds.ToolSoccerBall,
                Key.Key9 => ContentIds.ToolDrink,
                Key.Key0 => ContentIds.ToolRepairKit,
                _ => null,
            };
            if (launchable is not null)
            {
                if (LauncherTool is not null && GodotObject.IsInstanceValid(LauncherTool))
                {
                    if (!_sawPointerInput)
                        _cursor = GetViewport().GetMousePosition();
                    _sawPointerInput = true;
                    _pendingLaunchableSpawn = launchable;
                }
                return;
            }

            ToolId? selected = key.PhysicalKeycode switch
            {
                // Shift+G matches LaboratoryControlComponent: same tool, more behind it.
                Key.G => key.ShiftPressed ? ToolId.PowerGrab : ToolId.Grab,
                Key.B => ToolId.BoxingGlove,
                Key.K => ToolId.BaseballBat,
                Key.J => ToolId.Pistol,
                Key.F => ToolId.Pet,
                Key.T => ToolId.Tickle,
                // S, not the plan's suggested H: H already toggles the laboratory telemetry
                // panel, and one key doing two unrelated things is the kind of collision
                // that only shows up as a mystery half-way through a tuning session.
                Key.S => ToolId.FireSprayer,
                _ => null,
            };
            if (selected.HasValue)
                Pipeline.SelectTool(selected.Value);
        }
    }

    /// <summary>
    /// Applies the pointer's pending press/drag/release against the tether. Called
    /// by <see cref="App.BuddyLab"/> from the physics step so the space-state pick
    /// query is valid; safe to call every tick regardless of pause state.
    /// </summary>
    public void ResolvePendingInput()
    {
        if (!_active)
        {
            return;
        }

        if (!Grab.IsGrabbing)
            _ownsGrab = false;

        Vector2 cursor = GetViewport().GetCanvasTransform().AffineInverse() * _cursor;
        WorldCursor = cursor;

        ToolId tool = Pipeline is not null && GodotObject.IsInstanceValid(Pipeline)
            ? Pipeline.SelectedTool
            : ToolId.Grab;

        if (LauncherTool is not null && GodotObject.IsInstanceValid(LauncherTool) &&
            _sawPointerInput)
        {
            LauncherTool.MovePointer(cursor);
            if (_pendingLaunchableSpawn is not null)
            {
                LauncherTool.RequestSpawn(_pendingLaunchableSpawn, cursor);
                _pendingLaunchableSpawn = null;
            }
        }

        if (CareCursorVisual is not null && GodotObject.IsInstanceValid(CareCursorVisual))
            CareCursorVisual.SetPointerState(tool, cursor, CareToolShown(tool));
        if (CareCursor is not null && GodotObject.IsInstanceValid(CareCursor))
            CareCursor.SetPointerState(tool, cursor, CareToolShown(tool));

        // Forward pointer state to the non-grab tools only after real pointer
        // input has been seen; scenarios drive the tool APIs directly instead.
        if (_sawPointerInput)
        {
            if (CursorTools is not null && GodotObject.IsInstanceValid(CursorTools) &&
                CursorTools.DrivesTool(tool))
            {
                CursorTools.MoveCursor(cursor);
            }

            if (CareTool is not null && GodotObject.IsInstanceValid(CareTool))
            {
                CareTool.SetWiggle(tool == ToolId.Tickle && IsSecondaryHeld);
                // Dragging the feather across the buddy tickles as before; shaking it in place
                // with secondary does too, so a stationary hand is not a dead hand.
                bool careHeld = ToolCatalog.CareKindOf(tool) is not null &&
                    (IsPrimaryHeld || (tool == ToolId.Tickle && IsSecondaryHeld));
                CareTool.SetStroke(careHeld, cursor);
            }

            if (GunTool is not null && GodotObject.IsInstanceValid(GunTool) &&
                GunTool.DrivesTool(tool))
            {
                // Primary is the trigger; the gun's own model turns the held state into
                // one shot per press. Wheel and reload intents arrive the same way.
                GunTool.MoveCursor(cursor);
                if (_pendingPress)
                {
                    // Only the level state is forwarded below, so a click whose press and
                    // release both landed between two routed ticks would never reach the
                    // gun at all. The press edge is routed in its own right.
                    GunTool.LatchTrigger();
                }

                GunTool.SetTriggerHeld(IsPrimaryHeld);
                if (_pendingReload)
                    GunTool.RequestReload();
                if (_pendingWheelSteps != 0)
                    GunTool.ApplyWheel(_pendingWheelSteps);
            }

            if (SprayerTool is not null && GodotObject.IsInstanceValid(SprayerTool) &&
                tool == ToolId.FireSprayer)
            {
                // No press edge and no reload: the stream simply follows the held state,
                // and releasing primary stops it on the same routed tick.
                SprayerTool.MoveCursor(cursor);
                if (_pendingPress)
                    SprayerTool.LatchPrimary();
                SprayerTool.SetPrimaryHeld(IsPrimaryHeld);
                if (_pendingWheelSteps != 0)
                    SprayerTool.ApplyWheel(_pendingWheelSteps);
            }
        }

        // Consumed whether or not a gun is selected: a reload pressed while the bat is
        // out must not fire the instant a gun is drawn.
        _pendingReload = false;
        _pendingWheelSteps = 0;

        // Same rule the cursor anchor already follows: forward pointer state to
        // the tools only once real pointer input has been seen, so a headless
        // scenario driving the tool APIs directly is never clobbered by a
        // synthetic "nothing is held".
        bool swingTool = _sawPointerInput &&
                         CursorTools is not null &&
                         GodotObject.IsInstanceValid(CursorTools) &&
                         CursorTools.IsSwingCapableTool(tool);

        // Charging is guarded on the grab and aim being idle, and that is not
        // redundant with the launcher branch below. CanAimCurrentGrab inspects
        // only the current grab and is not tied to the selected tool, so grabbing
        // a Baseball, beginning an aim, and then selecting the bat leaves a live
        // aim while a swing-capable tool is selected. Routing secondary to charge
        // unconditionally would swallow the RequestRelease that fires the
        // launcher and strand the aim with no way to release it. The bat simply
        // refuses to charge while a grab or aim is outstanding.
        bool swingOwnsSecondary = swingTool &&
                                  !Grab.IsGrabbing &&
                                  (LauncherTool is null ||
                                   !GodotObject.IsInstanceValid(LauncherTool) ||
                                   !LauncherTool.IsAiming);

        if (_pendingSecondaryPress)
        {
            _pendingSecondaryPress = false;
            if (swingOwnsSecondary)
            {
                CursorTools!.SetChargeHeld(true);
            }
            else if (LauncherTool is not null && GodotObject.IsInstanceValid(LauncherTool) &&
                     !Grab.IsGrabbing && !LauncherTool.IsAiming &&
                     LauncherTool.CanSpawn(ContentIds.ForTool(tool)))
            {
                // Any selected launchable + RMB with empty hands places one at the pointer: the
                // grenade's chord was never grenade-specific, and the Baseball reaching for a
                // number key while every other tool used the mouse was the bug (owner feedback
                // 2026-08-19). The placed object stays inert until the player LMB-grabs it and
                // RMB begins the existing pullback chord.
                LauncherTool.RequestSpawn(ContentIds.ForTool(tool), cursor);
            }
            else if (LauncherTool is not null &&
                GodotObject.IsInstanceValid(LauncherTool) &&
                LauncherTool.CanAimCurrentGrab)
            {
                LauncherTool.RequestBegin(cursor);
                // The same press that begins the pullback pulls the pin, which is why a
                // grenade has no separate arming input: every pullback-launched grenade is
                // live, and every inert one was thrown by hand. The grenade's own model
                // ignores the request unless it is holding a pinned grenade.
                if (GrenadeTool is not null && GodotObject.IsInstanceValid(GrenadeTool))
                    GrenadeTool.RequestPinPull();
            }
            else
            {
                // Outside the grabbed-launchable launcher chord, secondary keeps
                // its established cancel/drop behavior.
                _pendingPress = false;
                _pendingRelease = false;
                ReleaseIfGrabbing(countsAsThrow: false);
                if (LauncherTool is not null && GodotObject.IsInstanceValid(LauncherTool))
                    LauncherTool.RequestCancel();
            }
        }

        if (_pendingSecondaryRelease)
        {
            _pendingSecondaryRelease = false;
            if (swingTool)
            {
                // Always released, even when the press was swallowed by an
                // outstanding aim: a charge that could be started but never let
                // go would be a stuck button.
                CursorTools!.SetChargeHeld(false);
            }

            if (!swingOwnsSecondary &&
                LauncherTool is not null && GodotObject.IsInstanceValid(LauncherTool))
            {
                LauncherTool.RequestRelease();
            }
        }

        // A swing-capable tool is gripped for as long as it is equipped (owner feedback
        // 2026-08-20). Holding primary just to keep the bat upright was pure ceremony: the
        // player equips the bat, the bat is in hand, and right mouse is the only chord.
        if (swingTool)
        {
            CursorTools!.SetGrip(true);
        }

        if (_pendingPress)
        {
            _pendingPress = false;
            bool normalGrab = ToolCatalog.CategoryOf(tool) == ToolCategory.Grab;

            // A selected launchable picks up its own object with LMB, so placing one with RMB
            // and immediately grabbing it never needs a trip back to the Grab tool (owner
            // feedback 2026-08-19). This was already the grenade's behaviour; nothing about it
            // was grenade-specific. The filter keeps it to the tool's own object, so a selected
            // Baseball still cannot pick the buddy up by accident.
            string? launchableId = LauncherTool is not null &&
                GodotObject.IsInstanceValid(LauncherTool) &&
                LauncherTool.CanSpawn(ContentIds.ForTool(tool))
                    ? ContentIds.ForTool(tool)
                    : null;
            Func<RigidBody2D, bool>? selectedToolFilter = launchableId is null
                ? null
                : candidate => candidate is LooseObjectBody loose &&
                               loose.SemanticContentId == launchableId;

            if ((normalGrab || launchableId is not null) && !Grab.IsGrabbing &&
                TryPick(cursor, out RigidBody2D? body, selectedToolFilter))
            {
                if (Grab.TryGrab(body!, cursor, normalGrab && tool == ToolId.PowerGrab ? PowerProfile : null))
                {
                    _ownsGrab = true;
                    LastPickedPart = body is PuppetPartBody part ? part.PartId : null;
                    SuccessfulPickCount++;
                }
            }
        }

        if (_ownsGrab && Grab.IsGrabbing)
        {
            Grab.MoveCursor(cursor);
        }

        if (_pendingRelease)
        {
            _pendingRelease = false;
            if (LauncherTool is not null &&
                GodotObject.IsInstanceValid(LauncherTool) &&
                LauncherTool.IsAiming)
                LauncherTool.RequestCancel();
            ReleaseIfGrabbing(countsAsThrow: true);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_active || !Grab.IsGrabbing)
        {
            return;
        }

        // The harness node sits at the world origin, so world coordinates draw directly.
        GrabState grab = Grab.CurrentGrab;
        DrawLine(grab.GrabPoint, grab.CursorAnchor, TetherColor, 2.0f, true);
        DrawCircle(grab.CursorAnchor, 3.0f, TetherColor);
    }

    private void ReleaseIfGrabbing(bool countsAsThrow)
    {
        if (Grab.IsGrabbing)
        {
            Grab.Release(countsAsThrow);
        }
        _ownsGrab = false;
    }

    private bool TryPick(
        Vector2 world,
        out RigidBody2D? body,
        Func<RigidBody2D, bool>? selectedToolFilter = null)
    {
        body = null;
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
        {
            return false;
        }

        bool Allowed(RigidBody2D candidate) =>
            (PickFilter is null || PickFilter(candidate)) &&
            (selectedToolFilter is null || selectedToolFilter(candidate));

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = new CircleShape2D { Radius = PickRadius },
            Transform = new Transform2D(0.0f, world),
            CollisionMask = PickMask,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };

        Godot.Collections.Array<Godot.Collections.Dictionary> hits = space.IntersectShape(query, MaxPickResults);
        float bestDistance = float.MaxValue;
        foreach (Godot.Collections.Dictionary hit in hits)
        {
            if (hit.TryGetValue("collider", out Variant colliderValue) &&
                colliderValue.AsGodotObject() is RigidBody2D candidate && Allowed(candidate))
            {
                float distance = candidate.GlobalPosition.DistanceSquaredTo(world);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    body = candidate;
                }
            }
        }

        if (body is not null)
            return true;

        // Some headless physics backends can return no overlap during the first
        // synchronization frame. Fall back only when the real query found nothing. This fallback
        // is intentionally buddy-only; selected launchable-tool filters never make a buddy part
        // eligible, so a grenade click cannot accidentally grab the ragdoll through this path.
        float nearestDistance = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("buddy_parts"))
        {
            if (node is not RigidBody2D candidate || !Allowed(candidate))
                continue;
            float candidateDistance = candidate.GlobalPosition.DistanceSquaredTo(world);
            if (candidateDistance < nearestDistance)
            {
                nearestDistance = candidateDistance;
                body = candidate;
            }
        }
        return body is not null && nearestDistance <= Mathf.Pow(PickRadius + 24.0f, 2);
    }
}

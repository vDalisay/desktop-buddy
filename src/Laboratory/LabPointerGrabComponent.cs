using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Grab;
using DesktopBuddy.Interaction;
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

    // Optional tool routing (buddy lab only; the dual-profile lab wires none of
    // these and keeps the grab-only behavior). When present, the pointer feeds
    // the selected tool instead of always grabbing: care strokes and the glove
    // cursor follow real pointer input only, so headless scenarios that drive
    // the tool APIs directly are never clobbered by a stale (0,0) cursor.
    [Export] public InteractionDamageComponent? Pipeline { get; set; }
    [Export] public CursorToolController? CursorTools { get; set; }
    [Export] public CareStrokeComponent? CareTool { get; set; }
    [Export] public ToolCursorPresenter? CareCursor { get; set; }
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
        if (CareCursor is not null && GodotObject.IsInstanceValid(CareCursor))
        {
            ToolId tool = Pipeline is not null && GodotObject.IsInstanceValid(Pipeline)
                ? Pipeline.SelectedTool
                : ToolId.Grab;
            CareCursor.SetPointerState(tool, WorldCursor, false);
        }
        if (LauncherTool is not null && GodotObject.IsInstanceValid(LauncherTool))
            LauncherTool.RequestCancel();
    }

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
        }
        else if (@event.IsActionPressed(InputActions.Secondary))
        {
            _pendingSecondaryPress = true;
        }
        else if (@event.IsActionReleased(InputActions.Secondary))
        {
            _pendingSecondaryRelease = true;
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
        else if (Pipeline is not null && GodotObject.IsInstanceValid(Pipeline) &&
                 @event is InputEventKey { Pressed: true, Echo: false } key)
        {
            // One spawn key per launchable, all sharing the confirmed chord: the key only
            // places the object, Grab picks it up, secondary aims, release launches.
            string? launchable = key.PhysicalKeycode switch
            {
                Key.Key5 => ContentIds.ToolBaseball,
                Key.Key6 => ContentIds.ToolMeal,
                Key.Key7 => ContentIds.ToolGrenade,
                Key.Key8 => ContentIds.ToolSoccerBall,
                Key.Key9 => ContentIds.ToolDrink,
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
                Key.G => ToolId.Grab,
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

        if (CareCursor is not null && GodotObject.IsInstanceValid(CareCursor))
            CareCursor.SetPointerState(tool, cursor, _sawPointerInput && IsPrimaryHeld);

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
                CareTool.SetStroke(IsPrimaryHeld && ToolCatalog.CareKindOf(tool) is not null, cursor);
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
                // Outside the grabbed-Baseball launcher chord, secondary keeps
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

        // Primary grips a swing-capable tool by the handle. Nothing is displaced:
        // with a cursor tool selected, primary does nothing today.
        if (swingTool)
        {
            CursorTools!.SetGrip(IsPrimaryHeld);
        }

        if (_pendingPress)
        {
            _pendingPress = false;
            if (tool == ToolId.Grab && !Grab.IsGrabbing &&
                TryPick(cursor, out RigidBody2D? body))
            {
                if (Grab.TryGrab(body!, cursor))
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

    private bool TryPick(Vector2 world, out RigidBody2D? body)
    {
        body = null;
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
        {
            return false;
        }

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
                colliderValue.AsGodotObject() is RigidBody2D candidate &&
                (PickFilter is null || PickFilter(candidate)))
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
        // synchronization frame. Fall back only when the real query found nothing.
        float nearestDistance = float.MaxValue;
        foreach (Node node in GetTree().GetNodesInGroup("buddy_parts"))
        {
            if (node is not RigidBody2D candidate || (PickFilter is not null && !PickFilter(candidate)))
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

using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Sandbox;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Platform;

/// <summary>
/// Drives the desktop shell: it composes the window service and the Work/Play
/// <see cref="InputModeStateMachine"/>, translates shell input (the in-app mode
/// hotkey, Escape, clicks inside/outside the box, focus loss) into mode
/// transitions, and rebuilds the sandbox boundary on a physics boundary when the
/// window resizes (`ARCHITECTURE.md` §9). The mode hotkey/Escape are shell-owned
/// platform controls, distinct from the gameplay <c>ToolInputFrame</c> path that
/// the InputCollector will own — so reading them here does not violate the
/// single-input-reader rule for gameplay.
///
/// Recovery invariant (ROADMAP M2 exit): the tray/global-toggle/Escape paths can
/// always return the shell to Work Mode, so the user never loses control.
/// </summary>
public partial class DesktopShellController : Node
{
    private const string Category = "Shell";

    [Export] public DesktopWindowController Window { get; set; } = null!;
    [Export] public BoundaryController Boundaries { get; set; } = null!;

    private readonly InputModeStateMachine _mode = new(DomainInputMode.Work);
    private Rect2 _innerBounds;
    private Vector2I? _pendingClientSize;
    private double _storedZoom = 1.0;
    private double _effectiveZoom = 1.0;

    public DomainInputMode Mode => _mode.Current;
    public int ModeChangeCount { get; private set; }

    /// <summary>The client-pixel Work-Mode hit regions last sent to the window service (test observability).</summary>
    public IReadOnlyList<Rect2I> LastWorkModeHitRegions { get; private set; } = Array.Empty<Rect2I>();

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(Window) || !GodotObject.IsInstanceValid(Boundaries))
        {
            throw new InvalidOperationException("DesktopShellController requires an injected window controller and boundary.");
        }

        // Select the native Windows adapter on a real standalone run; headless,
        // editor, and non-Windows runs get the emulated adapter. Must precede any
        // window query (ResolvePlacement) below.
        Window.Configure(WindowsDesktopAdapterFactory.Create());

        Window.ClientBoundsChanged += OnClientBoundsChanged;
        Window.WindowFocusLost += OnWindowFocusLost;
        Boundaries.LayoutApplied += OnLayoutApplied;

        Vector2I clientSize = ResolveClientSize();

        // Apply the launch placement (first launch anchors lower-right) and the
        // transparent/borderless/topmost window flags with an opaque fallback.
        Rect2I placement = Window.ResolvePlacement(storedRect: null);
        Window.ApplyWindowSettings(WindowSettings.Defaults with { Rect = placement });

        Boundaries.Initialize(clientSize, _storedZoom);
        ApplyMode(force: true);

        Log.Info(Category, $"Shell composed (mode={_mode.Current} transparency={Window.TransparencyActive}).");
    }

    /// <summary>Routed from the sandbox fixed tick: apply a queued resize to the boundary.</summary>
    public void PhysicsTick()
    {
        if (_pendingClientSize is Vector2I size)
        {
            _pendingClientSize = null;
            Boundaries.RequestLayout(size, _storedZoom);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.ToggleInputMode))
        {
            Apply(ShellInputEvent.GlobalToggle);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        {
            Apply(ShellInputEvent.EscapePressed);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            bool insideBox = _innerBounds.HasPoint(ToWorld(click.Position));
            Apply(insideBox ? ShellInputEvent.BuddyInteraction : ShellInputEvent.OutsideClick);
        }
    }

    /// <summary>Return control to Work Mode from a tray action or any recovery path.</summary>
    public void ReturnToWorkMode() => Apply(ShellInputEvent.TrayReturnToWork);

    private void Apply(ShellInputEvent input)
    {
        if (_mode.Apply(input))
        {
            ModeChangeCount++;
            ApplyMode(force: false);
        }
    }

    private void ApplyMode(bool force)
    {
        Window.SetInputMode(_mode.Current, WorkModeHitRegions());
        if (force)
        {
            ModeChangeCount = 0;
        }
    }

    /// <summary>
    /// Work-Mode interactive regions in <b>client pixels</b> (what the native
    /// adapter's <c>WM_NCHITTEST</c> needs). The sandbox box is projected from
    /// world units through the effective zoom (`SandboxProjection`). Until the
    /// buddy and menu panels compose (later milestones), the visible box is the
    /// interactive region and the transparent exterior passes through; sub-regions
    /// (buddy, menu) are added when they exist.
    /// </summary>
    private IReadOnlyList<Rect2I> WorkModeHitRegions()
    {
        if (_innerBounds.Size == Vector2.Zero)
        {
            LastWorkModeHitRegions = Array.Empty<Rect2I>();
            return LastWorkModeHitRegions;
        }

        PixelRect box = SandboxProjection.SandboxRectToClient(
            _innerBounds.Position.X,
            _innerBounds.Position.Y,
            _innerBounds.Size.X,
            _innerBounds.Size.Y,
            _effectiveZoom);
        LastWorkModeHitRegions = new[] { new Rect2I(box.X, box.Y, box.Width, box.Height) };
        return LastWorkModeHitRegions;
    }

    private void OnClientBoundsChanged(Rect2I bounds) => _pendingClientSize = bounds.Size;

    private void OnWindowFocusLost() => Apply(ShellInputEvent.FocusLost);

    private void OnLayoutApplied(RoomLayout layout, Rect2 innerBounds)
    {
        _innerBounds = innerBounds;
        _effectiveZoom = layout.EffectiveZoom;
        // A resize/zoom rebuild re-derives the interactive region in client pixels.
        Window.SetInputMode(_mode.Current, WorkModeHitRegions());
    }

    private Vector2I ResolveClientSize()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;
        var size = new Vector2I((int)viewport.X, (int)viewport.Y);
        if (size.X < RoomLayoutPolicy.MinimumRoomWidth || size.Y < RoomLayoutPolicy.MinimumRoomHeight)
        {
            // Headless viewports can report zero before the first render frame.
            size = new Vector2I(RoomLayoutPolicy.DefaultClientWidth, RoomLayoutPolicy.DefaultClientHeight);
        }

        return size;
    }

    private Vector2 ToWorld(Vector2 viewportPoint) => GetViewport().GetCanvasTransform().AffineInverse() * viewportPoint;

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Window))
        {
            Window.ClientBoundsChanged -= OnClientBoundsChanged;
            Window.WindowFocusLost -= OnWindowFocusLost;
        }

        if (GodotObject.IsInstanceValid(Boundaries))
        {
            Boundaries.LayoutApplied -= OnLayoutApplied;
        }
    }
}

using DesktopBuddy.App;
using DesktopBuddy.Domain.Persistence;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Shipping input bridge for dropped physical tools. It attaches the focused transaction
/// component only after SandboxRoot has finished composing its existing gameplay services, then
/// queues the rebindable Drop Tool action and left-double-click re-equip onto a physics boundary.
/// </summary>
public partial class DroppedToolInputBootstrap : Node
{
    private SandboxRoot? _sandbox;
    private DroppedToolInteractionComponent? _droppedTools;
    private SwordImpalementComponent? _impalement;
    private bool _dropPending;
    private bool _reequipPending;
    private bool _bindingApplied;
    private Vector2 _reequipViewportPosition;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        // Wake until the shipping sandbox is attached once. Afterwards input wakes the bridge
        // only for the next transaction boundary instead of paying a 120 Hz idle callback.
        SetPhysicsProcess(true);
        SetProcessUnhandledInput(true);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.DropTool))
        {
            _dropPending = true;
            SetPhysicsProcess(true);
            return;
        }

        if (@event is InputEventMouseButton
            {
                Pressed: true,
                DoubleClick: true,
                ButtonIndex: MouseButton.Left,
            } mouse)
        {
            _reequipPending = true;
            _reequipViewportPosition = mouse.Position;
            SetPhysicsProcess(true);
        }
    }

    public override void _PhysicsProcess(double _delta)
    {
        if (!EnsureAttached())
            return;

        if (_dropPending)
        {
            _dropPending = false;
            // Guns drop where the player is pointing; a cursor tool ignores this and uses
            // the body it is already holding.
            _droppedTools!.TryDropSelected(_sandbox!.Pointer.WorldCursor);
        }

        if (_reequipPending)
        {
            _reequipPending = false;
            Vector2 world = _sandbox!.GetViewport().GetCanvasTransform().AffineInverse() *
                            _reequipViewportPosition;
            _droppedTools!.TryReequipAt(world);
        }

        if (!_dropPending && !_reequipPending)
            SetPhysicsProcess(false);
    }

    private bool EnsureAttached()
    {
        if (_sandbox is not null && !GodotObject.IsInstanceValid(_sandbox))
        {
            _sandbox = null;
            _droppedTools = null;
            _impalement = null;
            _bindingApplied = false;
        }
        if (_droppedTools is not null && GodotObject.IsInstanceValid(_droppedTools) &&
            _droppedTools.IsInitialized)
        {
            return true;
        }

        _sandbox ??= GetTree().Root.FindChild("Sandbox", true, false) as SandboxRoot;
        if (_sandbox is null || !GodotObject.IsInstanceValid(_sandbox) ||
            !_sandbox.Pipeline.IsInitialized || !_sandbox.Objects.IsInitialized ||
            !_sandbox.CursorTools.IsInitialized || !_sandbox.Grab.IsInitialized)
        {
            return false;
        }

        if (!_bindingApplied)
        {
            HotkeyBinding.Apply(
                InputActions.DropTool,
                LocalSettingsInputBindings.DropTool(_sandbox.Shell.CurrentLocalSettings));
            _bindingApplied = true;
        }

        _droppedTools = new DroppedToolInteractionComponent
        {
            Name = nameof(DroppedToolInteractionComponent),
        };
        _sandbox.AddChild(_droppedTools);
        _droppedTools.Initialize(
            _sandbox.Objects,
            _sandbox.Pipeline,
            _sandbox.CursorTools,
            _sandbox.Grab,
            _sandbox.Buddy);

        // The Sword's impalement rides along here because this is the one place that holds a
        // live dropped-tool component: an impaled blade is a dropped blade that has been
        // pinned, so it needs the very transaction this bootstrap owns.
        if (_sandbox.Gore.IsInitialized)
        {
            _impalement = new SwordImpalementComponent { Name = nameof(SwordImpalementComponent) };
            _sandbox.AddChild(_impalement);
            _impalement.Initialize(
                _sandbox.Pipeline,
                _sandbox.Buddy,
                _sandbox.CursorTools,
                _droppedTools,
                _sandbox.Grab,
                _sandbox.CursorToolVisual,
                _sandbox.LooseObjectVisual);
        }

        return true;
    }
}
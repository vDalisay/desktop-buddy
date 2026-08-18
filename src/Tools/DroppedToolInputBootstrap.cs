using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Shipping input bridge for dropped physical tools. It attaches the focused transaction
/// component only after SandboxRoot has finished composing its existing gameplay services, then
/// queues D-to-drop and left-double-click re-equip onto a physics boundary.
/// </summary>
public partial class DroppedToolInputBootstrap : Node
{
    private SandboxRoot? _sandbox;
    private DroppedToolInteractionComponent? _droppedTools;
    private bool _dropPending;
    private bool _reequipPending;
    private Vector2 _reequipViewportPosition;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetPhysicsProcess(true);
        SetProcessUnhandledInput(true);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.DropTool))
        {
            _dropPending = true;
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
        }
    }

    public override void _PhysicsProcess(double _delta)
    {
        if (!EnsureAttached())
            return;

        if (_dropPending)
        {
            _dropPending = false;
            _droppedTools!.TryDropSelected();
        }

        if (_reequipPending)
        {
            _reequipPending = false;
            Vector2 world = _sandbox!.GetViewport().GetCanvasTransform().AffineInverse() *
                            _reequipViewportPosition;
            _droppedTools!.TryReequipAt(world);
        }
    }

    private bool EnsureAttached()
    {
        if (_sandbox is not null && !GodotObject.IsInstanceValid(_sandbox))
        {
            _sandbox = null;
            _droppedTools = null;
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
        return true;
    }
}

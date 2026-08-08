using System;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

public partial class EnvironmentPlacementController : Node
{
    private EnvironmentEditSession? _session;
    private EnvironmentDecorationResource? _definition;
    private ColorRect? _ghost;
    private Node? _ghostParent;
    private RoomScreenBounds _room;

    public bool Active { get; private set; }
    public bool GhostValid { get; private set; }
    public CanonicalRoomPosition GhostPosition { get; private set; }
    public bool SnapEnabled { get; set; }
    public EnvironmentGridSize GridSize { get; set; } = EnvironmentGridSize.Medium;

    public event Action? Changed;
    public event Action<EnvironmentEditResult>? PlacementCommitted;

    public void Configure(EnvironmentEditSession session, Node ghostParent, in RoomScreenBounds room)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _ghostParent = ghostParent ?? throw new ArgumentNullException(nameof(ghostParent));
        UpdateRoom(room);
    }

    public void UpdateRoom(in RoomScreenBounds room)
    {
        if (!room.IsValid) throw new ArgumentException("Room bounds are invalid.", nameof(room));
        _room = room;
        if (Active && _ghost is not null)
        {
            (float x, float y) = EnvironmentPlacement.ToScreen(GhostPosition, room);
            _ghost.Position = new Vector2(x, y) - _ghost.Size * .5f;
        }
    }

    public void Begin(EnvironmentDecorationResource definition)
    {
        if (_session is null || _ghostParent is null)
            throw new InvalidOperationException("Placement controller is not configured.");
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _ghost?.QueueFree();
        _ghost = new ColorRect
        {
            Name = "EnvironmentPlacementGhost",
            Color = new Color(definition.PrimaryColor, .55f),
            Size = definition.VisualSize,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _ghost.PivotOffset = definition.VisualSize * .5f;
        _ghostParent.AddChild(_ghost);
        _ghost.ZIndex = 100;
        Active = true;
        GhostValid = false;
        _ghost.Visible = false;
        Changed?.Invoke();
    }

    public bool UpdatePointer(Vector2 screenPosition)
    {
        if (!Active || _definition is null || _ghost is null) return false;
        DecorationDefinition definition = _definition.ToDefinition();
        GhostValid = EnvironmentPlacement.TryMap(screenPosition.X, screenPosition.Y, _room,
            definition.AnchorKind, SnapEnabled, GridSize, out CanonicalRoomPosition canonical);
        if (GhostValid)
        {
            GhostPosition = canonical;
            (float x, float y) = EnvironmentPlacement.ToScreen(canonical, _room);
            _ghost.Position = new Vector2(x, y) - _ghost.Size * .5f;
        }
        else _ghost.Position = screenPosition - _ghost.Size * .5f;
        _ghost.Visible = true;
        _ghost.Modulate = GhostValid ? Colors.White : new Color(1, .35f, .35f, 1);
        Changed?.Invoke();
        return GhostValid;
    }

    public EnvironmentEditResult CommitGhost()
    {
        if (!Active || !GhostValid || _definition is null || _session is null)
            return new EnvironmentEditResult(EnvironmentEditStatus.InvalidPlacement);
        EnvironmentEditResult result = _session.Place(_definition.ToDefinition().Id, GhostPosition);
        PlacementCommitted?.Invoke(result);
        Changed?.Invoke();
        return result;
    }

    public void Cancel()
    {
        Active = false;
        GhostValid = false;
        _definition = null;
        if (GodotObject.IsInstanceValid(_ghost)) _ghost!.QueueFree();
        _ghost = null;
        Changed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if (!Active) return;
        switch (input)
        {
            case InputEventMouseMotion motion:
                UpdatePointer(motion.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                UpdatePointer(click.Position);
                CommitGhost();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }:
                Cancel();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }:
                Cancel();
                GetViewport().SetInputAsHandled();
                break;
        }
    }
}

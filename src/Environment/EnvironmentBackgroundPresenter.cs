using DesktopBuddy.Domain.Environment;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Environment;

public partial class EnvironmentBackgroundPresenter : CanvasLayer
{
    private ColorRect _wall = null!;
    private ColorRect _floor = null!;
    private Win98WindowFrame? _frame;
    private EnvironmentBackground _background = EnvironmentBackground.Default;
    public EnvironmentBackground Current => _background;

    public override void _Ready()
    {
        Layer = -50;
        _wall = Rect("EnvironmentWall");
        _floor = Rect("EnvironmentFloor");
        AddChild(_wall);
        AddChild(_floor);
        Apply(_background);
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_frame))
            _frame = GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
        Rect2 room = GodotObject.IsInstanceValid(_frame)
            ? _frame!.ContentViewportRect
            : new Rect2(Vector2.Zero, GetViewport().GetVisibleRect().Size);
        float split = room.Position.Y + room.Size.Y * .72f;
        _wall.Position = room.Position;
        _wall.Size = new Vector2(room.Size.X, split - room.Position.Y);
        _floor.Position = new Vector2(room.Position.X, split);
        _floor.Size = new Vector2(room.Size.X, room.End.Y - split);
    }

    public void Apply(in EnvironmentBackground background)
    {
        _background = background;
        if (!GodotObject.IsInstanceValid(_wall)) return;
        _wall.Color = ToGodot(background.Wall);
        _floor.Color = ToGodot(background.Floor);
    }

    private static ColorRect Rect(string name) => new()
    {
        Name = name,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        ShowBehindParent = true,
    };

    private static Color ToGodot(EnvironmentColor color) =>
        Color.Color8(color.Red, color.Green, color.Blue, color.Alpha);
}

using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>Throwaway M1 Windows transparency/client-coordinate validation scene.</summary>
public partial class TransparentWindowSpike : Node2D
{
    private Label _readout = null!;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(480, 360);
        GetWindow().Borderless = true;
        GetWindow().AlwaysOnTop = true;
        GetWindow().Transparent = true;
        GetViewport().TransparentBg = true;
        _readout = GetNode<Label>("Readout");
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        Vector2 client = GetViewport().GetMousePosition();
        Vector2I screen = DisplayServer.MouseGetPosition();
        float scale = DisplayServer.ScreenGetScale(DisplayServer.WindowGetCurrentScreen());
        _readout.Text = $"client {client.X:F1}, {client.Y:F1}\nscreen {screen.X}, {screen.Y}\nDPI scale {scale:F2}";
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(12, 12, 456, 336), new Color(0.2f, 0.8f, 1, 0.9f), false, 2);
        DrawCircle(new Vector2(240, 180), 42, new Color(1, 0.45f, 0.2f, 0.9f));
        DrawRect(new Rect2(36, 280, 160, 42), new Color(0.2f, 0.25f, 0.35f, 0.9f));
    }
}

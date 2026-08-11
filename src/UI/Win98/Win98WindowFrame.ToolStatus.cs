using Godot;

namespace DesktopBuddy.UI.Win98;

public partial class Win98WindowFrame
{
    private Label? _toolStatusLabel;
    private string _pendingToolStatus = string.Empty;

    /// <summary>
    /// Dedicated right-hand status-bar segment for persistent gameplay context. Transient
    /// messages continue to use StatusText on the left and therefore no longer erase the
    /// currently equipped tool.
    /// </summary>
    public string ToolStatusText
    {
        get => GodotObject.IsInstanceValid(_toolStatusLabel)
            ? _toolStatusLabel!.Text
            : _pendingToolStatus;
        set
        {
            _pendingToolStatus = value ?? string.Empty;
            EnsureToolStatusSegment();
            if (GodotObject.IsInstanceValid(_toolStatusLabel))
                _toolStatusLabel!.Text = _pendingToolStatus;
        }
    }

    private void EnsureToolStatusSegment()
    {
        if (GodotObject.IsInstanceValid(_toolStatusLabel) ||
            !GodotObject.IsInstanceValid(_statusLabel) ||
            _statusLabel.GetParent() is not PanelContainer status)
        {
            return;
        }

        var row = new HBoxContainer
        {
            Name = "StatusSegments",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddThemeConstantOverride("separation", 4);
        status.AddChild(row);
        _statusLabel.Reparent(row, false);
        _statusLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

        var separator = new VSeparator
        {
            MouseFilter = MouseFilterEnum.Ignore,
            CustomMinimumSize = new Vector2(4, 0),
        };
        row.AddChild(separator);

        _toolStatusLabel = new Label
        {
            Name = "ActiveToolStatusText",
            Text = _pendingToolStatus,
            CustomMinimumSize = new Vector2(150, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        row.AddChild(_toolStatusLabel);
    }
}

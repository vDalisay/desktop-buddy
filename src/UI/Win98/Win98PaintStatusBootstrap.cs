using System;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Keeps the shared Win98 status bar useful while the paint workspace is active. This is kept
/// outside CharacterEditorHost so the editor's persistence and rendering code remain unchanged.
/// </summary>
public partial class Win98PaintStatusBootstrap : Node
{
    private const double RefreshIntervalSeconds = 0.1;

    private CharacterEditorHost? _host;
    private PaintCanvasControl? _canvas;
    private Win98WindowFrame? _frame;
    private double _refreshRemaining;
    private bool _paintStatusActive;
    private string _lastStatus = string.Empty;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _refreshRemaining -= delta;
        if (_refreshRemaining > 0.0)
            return;
        _refreshRemaining = RefreshIntervalSeconds;

        ResolveNodes();
        bool active = IsPaintWorkspaceActive();
        if (!active)
        {
            if (_paintStatusActive && GodotObject.IsInstanceValid(_frame))
            {
                _frame!.StatusText = "Ready";
                _frame.WindowTitle = "Desktop Buddy";
            }

            _paintStatusActive = false;
            _lastStatus = string.Empty;
            return;
        }

        _paintStatusActive = true;
        string status = BuildStatus();
        if (!string.Equals(status, _lastStatus, StringComparison.Ordinal))
        {
            _frame!.StatusText = status;
            _lastStatus = status;
        }

        _frame!.WindowTitle = "Desktop Buddy - Paint";
    }

    private void ResolveNodes()
    {
        if (!GodotObject.IsInstanceValid(_host))
            _host = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;

        if (!GodotObject.IsInstanceValid(_canvas))
            _canvas = GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;

        if (!GodotObject.IsInstanceValid(_frame))
            _frame = GetTree().Root.FindChild(nameof(Win98WindowFrame), true, false) as Win98WindowFrame;
    }

    private bool IsPaintWorkspaceActive() =>
        GodotObject.IsInstanceValid(_host) &&
        GodotObject.IsInstanceValid(_canvas) &&
        GodotObject.IsInstanceValid(_frame) &&
        _host!.IsEditorOpen &&
        _canvas!.IsVisibleInTree();

    private string BuildStatus()
    {
        PaintWorkspace workspace = _canvas!.Workspace;
        string tool = _canvas.PanToolActive
            ? "Hand"
            : workspace.SelectedTool == PaintTool.Eraser ? "Eraser" : "Brush";
        string target = FormatPart(_canvas.ActivePartFilter);
        int zoomPercent = Mathf.RoundToInt((float)(_canvas.View.Zoom * 100.0));
        int rotation = ResolveQuarterTurnRotation();
        string dirty = workspace.IsDirty ? "Modified" : "Saved";

        return $"{tool}  |  Target: {target}  |  Size: {workspace.BrushDiameter}px  |  " +
               $"Zoom: {zoomPercent}%  |  Rotation: {rotation}°  |  {dirty}";
    }

    private int ResolveQuarterTurnRotation()
    {
        if (!GodotObject.IsInstanceValid(_host?.PreviewRig))
            return 0;

        int degrees = Mathf.RoundToInt(_host!.PreviewRig.RotationDegrees.Y);
        degrees %= 360;
        if (degrees < 0)
            degrees += 360;
        return ((degrees + 45) / 90 * 90) % 360;
    }

    private static string FormatPart(PaintPart? part) => part switch
    {
        null => "All body parts",
        PaintPart.Head => "Head",
        PaintPart.Torso => "Torso",
        PaintPart.LeftHand => "Left hand",
        PaintPart.RightHand => "Right hand",
        PaintPart.LeftFoot => "Left foot",
        PaintPart.RightFoot => "Right foot",
        _ => part.Value.ToString(),
    };
}

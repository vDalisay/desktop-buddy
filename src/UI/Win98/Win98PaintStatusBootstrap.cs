using System;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Keeps the shared Win98 status bar useful while the paint workspace is active.
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
    private string _lastTitle = string.Empty;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _refreshRemaining -= delta;
        if (_refreshRemaining > 0.0) return;
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
            _lastTitle = string.Empty;
            return;
        }

        _paintStatusActive = true;
        string status = BuildStatus();
        if (!string.Equals(status, _lastStatus, StringComparison.Ordinal))
        {
            _frame!.StatusText = status;
            _lastStatus = status;
        }

        string title = _canvas!.Workspace.IsDirty
            ? "Desktop Buddy - Paint *"
            : "Desktop Buddy - Paint";
        if (!string.Equals(title, _lastTitle, StringComparison.Ordinal))
        {
            _frame!.WindowTitle = title;
            _lastTitle = title;
        }
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
        string? focusedHelp = FocusedControlHelp();
        if (!string.IsNullOrWhiteSpace(focusedHelp))
            return focusedHelp;

        PaintWorkspace workspace = _canvas!.Workspace;
        string tool = _canvas.PanToolActive
            ? "Hand"
            : _canvas.EyedropperToolActive
                ? "Pick Color"
                : workspace.SelectedTool switch
                {
                    PaintTool.Brush => "Brush",
                    PaintTool.Pen => "Pen",
                    PaintTool.Spray => "Spray",
                    PaintTool.Curve => "Curve",
                    PaintTool.Eraser => "Eraser",
                    PaintTool.Fill => "Bucket Fill",
                    _ => "Paint",
                };
        string target = FormatPart(_canvas.ActivePartFilter);
        string hover = _canvas.HoveredPart is PaintPart hovered
            ? FormatPart(hovered)
            : "No paintable surface";
        int zoomPercent = Mathf.RoundToInt((float)(_canvas.View.Zoom * 100.0));
        int rotation = ResolveQuarterTurnRotation();
        string dirty = workspace.IsDirty ? "Modified" : "Saved";
        string curve = _canvas.CurvePending ? $"  |  {FormatCurvePhase(_canvas.CurvePhase)}" : string.Empty;

        return $"{tool}  |  Target: {target}  |  Hover: {hover}  |  Size: {workspace.BrushDiameter}px  |  " +
               $"Zoom: {zoomPercent}%  |  Rotation: {rotation}°  |  {dirty}{curve}";
    }

    private string? FocusedControlHelp()
    {
        Control? focus = GetViewport().GuiGetFocusOwner();
        if (!GodotObject.IsInstanceValid(focus) ||
            ReferenceEquals(focus, _canvas) ||
            !GodotObject.IsInstanceValid(_host) ||
            !_host!.IsAncestorOf(focus))
        {
            return null;
        }

        string help = !string.IsNullOrWhiteSpace(focus!.TooltipText)
            ? focus.TooltipText
            : focus.AccessibilityDescription;
        return string.IsNullOrWhiteSpace(help) ? null : $"Help: {help}";
    }

    private int ResolveQuarterTurnRotation()
    {
        if (!GodotObject.IsInstanceValid(_host?.PreviewRig)) return 0;

        int degrees = Mathf.RoundToInt(_host!.PreviewRig.RotationDegrees.Y);
        degrees %= 360;
        if (degrees < 0) degrees += 360;
        return ((degrees + 45) / 90 * 90) % 360;
    }

    private static string FormatCurvePhase(BuddyPaintCurvePhase phase) => phase switch
    {
        BuddyPaintCurvePhase.BaselineDragging => "Curve baseline",
        BuddyPaintCurvePhase.AwaitFirstBend => "Curve: set first bend",
        BuddyPaintCurvePhase.FirstBendDragging => "Curve first bend",
        BuddyPaintCurvePhase.AwaitSecondBend => "Curve: set second bend",
        BuddyPaintCurvePhase.SecondBendDragging => "Curve second bend",
        _ => "Curve",
    };

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

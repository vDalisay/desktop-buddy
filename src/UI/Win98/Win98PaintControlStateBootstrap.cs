using System;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Keeps bounded paint controls honest. The same source buttons are used by the toolbar,
/// shortcuts and File/Edit/View menus, so updating their disabled state updates every entry
/// point without duplicating command policy.
/// </summary>
public partial class Win98PaintControlStateBootstrap : Node
{
    private const double RefreshIntervalSeconds = 0.05;
    private const double Epsilon = 0.0001;

    private PaintCanvasControl? _canvas;
    private Button? _sizeDecrease;
    private Button? _sizeIncrease;
    private Button? _zoomOut;
    private Button? _zoomIn;
    private Button? _resetView;
    private double _refreshRemaining;

    public override void _Ready()
    {
        // CharacterEditorModeCoordinator pauses the gameplay tree while this UI is active.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        _refreshRemaining -= delta;
        if (_refreshRemaining > 0.0)
            return;
        _refreshRemaining = RefreshIntervalSeconds;

        ResolveNodes();
        if (!GodotObject.IsInstanceValid(_canvas))
            return;

        PaintWorkspace workspace = _canvas!.Workspace;
        bool minimumBrush = workspace.BrushDiameter <= PaintPolicy.MinBrushDiameter;
        bool maximumBrush = workspace.BrushDiameter >= PaintPolicy.MaxBrushDiameter;
        bool minimumZoom = _canvas.View.Zoom <= PaintViewState.MinimumZoom + Epsilon;
        bool maximumZoom = _canvas.View.Zoom >= PaintViewState.MaximumZoom - Epsilon;

        SetState(
            _sizeDecrease,
            minimumBrush,
            minimumBrush ? "Brush size is already at the minimum." : "Decrease brush size.");
        SetState(
            _sizeIncrease,
            maximumBrush,
            maximumBrush ? "Brush size is already at the maximum." : "Increase brush size.");
        SetState(
            _zoomOut,
            minimumZoom,
            minimumZoom ? "The canvas is already at minimum zoom." : "Zoom out of the buddy canvas.");
        SetState(
            _zoomIn,
            maximumZoom,
            maximumZoom ? "The canvas is already at maximum zoom." : "Zoom in on the buddy canvas.");

        PaintPoint pan = _canvas.View.Pan;
        bool defaultView = minimumZoom &&
            Math.Abs(pan.X) <= Epsilon &&
            Math.Abs(pan.Y) <= Epsilon;
        SetState(
            _resetView,
            defaultView,
            defaultView ? "The canvas view is already at its default position and zoom." : "Reset canvas zoom and pan.");
    }

    private void ResolveNodes()
    {
        if (!GodotObject.IsInstanceValid(_canvas))
        {
            _canvas = Find<PaintCanvasControl>("CharacterPaintCanvas");
            _sizeDecrease = null;
            _sizeIncrease = null;
            _zoomOut = null;
            _zoomIn = null;
            _resetView = null;
        }

        _sizeDecrease ??= Find<Button>("PaintSizeDecreaseButton");
        _sizeIncrease ??= Find<Button>("PaintSizeIncreaseButton");
        _zoomOut ??= Find<Button>("PaintZoomOutButton");
        _zoomIn ??= Find<Button>("PaintZoomInButton");
        _resetView ??= Find<Button>("PaintResetViewButton");
    }

    private T? Find<T>(string name) where T : Node =>
        GetTree().Root.FindChild(name, recursive: true, owned: false) as T;

    private static void SetState(Button? button, bool disabled, string tooltip)
    {
        if (!GodotObject.IsInstanceValid(button))
            return;

        button!.TooltipText = tooltip;
        if (button.Disabled == disabled)
            return;

        button.Disabled = disabled;
        if (disabled && button.GetNodeOrNull<Timer>("Repeat") is Timer repeat)
            repeat.Stop();
    }
}
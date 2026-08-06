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
        SetDisabled(_sizeDecrease, workspace.BrushDiameter <= PaintPolicy.MinBrushDiameter);
        SetDisabled(_sizeIncrease, workspace.BrushDiameter >= PaintPolicy.MaxBrushDiameter);
        SetDisabled(_zoomOut, _canvas.View.Zoom <= PaintViewState.MinimumZoom + Epsilon);
        SetDisabled(_zoomIn, _canvas.View.Zoom >= PaintViewState.MaximumZoom - Epsilon);

        PaintPoint pan = _canvas.View.Pan;
        bool defaultView = _canvas.View.Zoom <= PaintViewState.MinimumZoom + Epsilon &&
            Math.Abs(pan.X) <= Epsilon &&
            Math.Abs(pan.Y) <= Epsilon;
        SetDisabled(_resetView, defaultView);
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

    private static void SetDisabled(Button? button, bool disabled)
    {
        if (!GodotObject.IsInstanceValid(button) || button!.Disabled == disabled)
            return;

        button.Disabled = disabled;
        if (disabled && button.GetNodeOrNull<Timer>("Repeat") is Timer repeat)
            repeat.Stop();
    }
}

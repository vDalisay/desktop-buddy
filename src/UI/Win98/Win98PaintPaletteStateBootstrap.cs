using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Mirrors the current foreground color into the palette's current-color block, including the
/// changes the palette never sees — the eyedropper, and the color wheel.
///
/// <para>The selection ring on the palette blocks is deliberately NOT drawn here.
/// <see cref="Win98PaintCustomPaletteBootstrap"/> creates, colours and rebuilds those blocks, so
/// it owns their pressed state too. This class used to mark them as well, from a cached button
/// list that went stale the moment the palette rebuilt — the cached buttons were freed, nothing
/// was ever un-marked, and every colour the player had ever clicked stayed ringed (owner report
/// 2026-08-19).</para>
/// </summary>
public partial class Win98PaintPaletteStateBootstrap : Node
{
    private const double RefreshIntervalSeconds = 0.05;

    private PaintCanvasControl? _canvas;
    private ColorRect? _currentColor;
    private double _refreshRemaining;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _refreshRemaining -= delta;
        if (_refreshRemaining > 0.0)
            return;
        _refreshRemaining = RefreshIntervalSeconds;

        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        _currentColor ??= GetTree().Root.FindChild("PaintCurrentColor", true, false) as ColorRect;
        if (!GodotObject.IsInstanceValid(_canvas) || !GodotObject.IsInstanceValid(_currentColor))
            return;

        PaintColor selected = _canvas!.Workspace.SelectedColor;
        _currentColor!.Color = new Color(selected.R / 255f, selected.G / 255f, selected.B / 255f, 1f);
    }
}

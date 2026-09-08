using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Environment;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Every reference the walkthrough makes to the Paint Background workspace, in one place.
///
/// <para><c>src/Environment</c> is compiled out of the reduced itch.io distribution, so the
/// controller may not name <see cref="EnvironmentBackgroundEditor"/> or
/// <see cref="EnvironmentBackgroundPresenter"/> anywhere else. That build gets
/// <c>FirstSessionGuidanceController.BackgroundAbsent.cs</c> instead.</para>
///
/// <para>The Paint Background steps are already absent from the itch step sequence, so none of this
/// is reachable there; the seam exists so the <em>types</em> can leave the assembly.</para>
/// </summary>
public sealed partial class FirstSessionGuidanceController
{
    private EnvironmentBackgroundEditor? _backgroundEditor;
    private EnvironmentBackgroundPresenter? _backgroundPresenter;
    private bool _backgroundSignalsBound;
    private EnvironmentColor? _backgroundColorOrigin;

    private void DiscoverBackground()
    {
        _backgroundEditor ??= GetTree().Root.FindChild(
            nameof(EnvironmentBackgroundEditor), true, false) as EnvironmentBackgroundEditor;
        _backgroundPresenter ??= GetTree().Root.FindChild(
            nameof(EnvironmentBackgroundPresenter), true, false) as EnvironmentBackgroundPresenter;
    }

    private void BindBackgroundSignals()
    {
        if (_backgroundSignalsBound || !GodotObject.IsInstanceValid(_backgroundEditor) ||
            _backgroundEditor!.FindChild("PaintSaveButton", true, false) is not Button save)
        {
            return;
        }

        save.Pressed += OnBackgroundSavePressed;
        _backgroundSignalsBound = true;
    }

    private void UnbindBackgroundSignals()
    {
        if (_backgroundSignalsBound && GodotObject.IsInstanceValid(_backgroundEditor) &&
            _backgroundEditor!.FindChild("PaintSaveButton", true, false) is Button save)
        {
            save.Pressed -= OnBackgroundSavePressed;
        }
    }

    private bool IsBackgroundOpen() =>
        GodotObject.IsInstanceValid(_backgroundEditor) && _backgroundEditor!.IsOpen &&
        GodotObject.IsInstanceValid(_backgroundPresenter);

    /// <summary>The editor itself, without the presenter check <see cref="IsBackgroundOpen"/> adds.</summary>
    private bool IsBackgroundEditorOpen() =>
        GodotObject.IsInstanceValid(_backgroundEditor) && _backgroundEditor!.IsOpen;

    private bool IsBackgroundEditorClosed() =>
        GodotObject.IsInstanceValid(_backgroundEditor) && !_backgroundEditor!.IsOpen;

    private bool IsBackgroundSpraySelected() =>
        _backgroundPresenter!.Canvas.Tool == EnvironmentPaintTool.Spray;

    private bool IsBackgroundCanvasDirty() =>
        GodotObject.IsInstanceValid(_backgroundPresenter) && _backgroundPresenter!.Canvas.IsDirty;

    private bool IsBackgroundCanvasClean() =>
        GodotObject.IsInstanceValid(_backgroundPresenter) && !_backgroundPresenter!.Canvas.IsDirty;

    /// <summary>Any colour will do here — the lesson is the palette, not a particular hue.</summary>
    private bool HasChosenBackgroundColor()
    {
        if (!IsBackgroundOpen())
            return false;
        EnvironmentColor colour = _backgroundPresenter!.Canvas.Color;
        if (_backgroundColorOrigin is not EnvironmentColor origin)
        {
            _backgroundColorOrigin = colour;
            return false;
        }
        return colour != origin;
    }

    /// <summary>The spotlight's search root. A Node, not a Control: the editor is not one.</summary>
    private Node? BackgroundScope => _backgroundEditor;

    private Control? BackgroundPanel() =>
        GodotObject.IsInstanceValid(_backgroundEditor)
            ? _backgroundEditor!.FindChild("PaintBackgroundPanel", true, false) as Control
            : null;
}

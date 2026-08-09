using System;
using Godot;

namespace DesktopBuddy.Environment;

/// <summary>Keeps temporary Environment visibility changes reversible and local.</summary>
public sealed class EnvironmentPresentationVisibility
{
    private Node3D? _background;
    private Node3D? _decorations;
    private bool _backgroundWasVisible;
    private bool _decorationsWereVisible;
    private bool _workHidden;

    public void Configure(Node3D background, Node3D decorations)
    {
        _background = background ?? throw new ArgumentNullException(nameof(background));
        _decorations = decorations ?? throw new ArgumentNullException(nameof(decorations));
    }

    public void SetWorkCompanionActive(bool active)
    {
        if (active == _workHidden || !GodotObject.IsInstanceValid(_background) || !GodotObject.IsInstanceValid(_decorations)) return;
        _workHidden = active;
        if (active)
        {
            _backgroundWasVisible = _background!.Visible;
            _decorationsWereVisible = _decorations!.Visible;
            _background.Visible = false;
            _decorations.Visible = false;
        }
        else
        {
            _background!.Visible = _backgroundWasVisible;
            _decorations!.Visible = _decorationsWereVisible;
        }
    }
}

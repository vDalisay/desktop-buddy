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

    /// <summary>
    /// Configures the painted background and, when this distribution ships Room Decorator, its
    /// decoration layer. The decoration surface is optional so the physically reduced Steam Demo
    /// can keep Paint Background + Work Mode without retaining Room Decorator types.
    /// </summary>
    public void Configure(Node3D background, Node3D? decorations = null)
    {
        _background = background ?? throw new ArgumentNullException(nameof(background));
        _decorations = decorations;
    }

    public void SetWorkCompanionActive(bool active)
    {
        if (active == _workHidden || !GodotObject.IsInstanceValid(_background)) return;
        _workHidden = active;
        bool hasDecorations = GodotObject.IsInstanceValid(_decorations);
        if (active)
        {
            _backgroundWasVisible = _background!.Visible;
            _background.Visible = false;
            if (hasDecorations)
            {
                _decorationsWereVisible = _decorations!.Visible;
                _decorations.Visible = false;
            }
        }
        else
        {
            _background!.Visible = _backgroundWasVisible;
            if (hasDecorations)
                _decorations!.Visible = _decorationsWereVisible;
        }
    }
}

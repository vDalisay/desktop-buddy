using System;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Platform;
using Godot;

namespace DesktopBuddy.Environment;

public partial class EnvironmentDecorator
{
    private DesktopShellController? _shell;
    private bool _applyingEnvironmentPreferences;

    public void ConfigurePreferences(DesktopShellController shell) =>
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));

    private void ApplySavedEnvironmentPreferences()
    {
        if (!GodotObject.IsInstanceValid(_snap) || !GodotObject.IsInstanceValid(_grid))
            return;

        // User testing found grid snapping added mode complexity without helping the demo flow.
        // Keep the old preference schema intact for migration/full-release reuse, but the current
        // public decorator is deliberately free-placement and ignores any previously saved snap flag.
        _applyingEnvironmentPreferences = true;
        try
        {
            _placement.SnapEnabled = false;
            _placement.GridSize = EnvironmentGridSize.Medium;
            _snap.ButtonPressed = false;
            _snap.Visible = false;
            _grid.Visible = false;
            _grid.Disabled = true;
        }
        finally
        {
            _applyingEnvironmentPreferences = false;
        }
    }

    private async void OnSnapPreferenceChanged(bool enabled)
    {
        // Retained only for backwards-compatible scene wiring. Hidden demo controls cannot enable
        // snapping; if a stale signal fires, force the current free-placement policy back on.
        _placement.SnapEnabled = false;
        _snap.ButtonPressed = false;
        _grid.Disabled = true;
        if (_applyingEnvironmentPreferences || _shell is null)
            return;
        await _shell.SaveEnvironmentPreferencesAsync(false, EnvironmentGridSize.Medium);
    }

    private async void OnGridPreferenceChanged(long index)
    {
        if (_applyingEnvironmentPreferences || _shell is null)
            return;
        await _shell.SaveEnvironmentPreferencesAsync(false, EnvironmentGridSize.Medium);
    }
}

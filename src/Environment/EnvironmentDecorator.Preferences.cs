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
        if (_shell is null || !GodotObject.IsInstanceValid(_snap) || !GodotObject.IsInstanceValid(_grid))
            return;

        (bool snapEnabled, EnvironmentGridSize gridSize) = _shell.EnvironmentDecoratorPreferences;
        _applyingEnvironmentPreferences = true;
        try
        {
            _placement.SnapEnabled = snapEnabled;
            _placement.GridSize = gridSize;
            _snap.ButtonPressed = snapEnabled;
            _grid.Disabled = !snapEnabled;
            for (int index = 0; index < _grid.ItemCount; index++)
            {
                if (_grid.GetItemId(index) != (int)gridSize) continue;
                _grid.Select(index);
                break;
            }
        }
        finally
        {
            _applyingEnvironmentPreferences = false;
        }
    }

    private async void OnSnapPreferenceChanged(bool enabled)
    {
        _placement.SnapEnabled = enabled;
        _grid.Disabled = !enabled;
        if (_applyingEnvironmentPreferences || _shell is null)
            return;
        await _shell.SaveEnvironmentPreferencesAsync(enabled, _placement.GridSize);
    }

    private async void OnGridPreferenceChanged(long index)
    {
        if (index < 0 || index >= _grid.ItemCount)
            return;
        _placement.GridSize = (EnvironmentGridSize)_grid.GetItemId((int)index);
        if (_applyingEnvironmentPreferences || _shell is null)
            return;
        await _shell.SaveEnvironmentPreferencesAsync(_placement.SnapEnabled, _placement.GridSize);
    }
}

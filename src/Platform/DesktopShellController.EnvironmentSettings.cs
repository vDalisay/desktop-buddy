using System;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;

namespace DesktopBuddy.Platform;

public partial class DesktopShellController
{
    /// <summary>
    /// Environment editor affordances are machine-local preferences. They deliberately live with
    /// the desktop shell settings instead of EnvironmentProgressState, so Reset Progress does not
    /// reset how the player likes to position furniture.
    /// </summary>
    public (bool SnapEnabled, EnvironmentGridSize GridSize) EnvironmentDecoratorPreferences =>
        (_settings.EnvironmentSnapToGrid, ValidEnvironmentGridSize(_settings.EnvironmentGridSize));

    public async Task SaveEnvironmentPreferencesAsync(bool snapEnabled, EnvironmentGridSize gridSize)
    {
        if (_saves is null)
            return;

        gridSize = ValidEnvironmentGridSize(gridSize);
        if (_settings.EnvironmentSnapToGrid == snapEnabled && _settings.EnvironmentGridSize == gridSize)
            return;

        _settings = _settings with
        {
            Revision = _settings.Revision == long.MaxValue ? long.MaxValue : _settings.Revision + 1,
            EnvironmentSnapToGrid = snapEnabled,
            EnvironmentGridSize = gridSize,
        };
        _saves.RegisterSettings(_settings);
        await _saves.SaveRegisteredSettingsAsync();
    }

    private static EnvironmentGridSize ValidEnvironmentGridSize(EnvironmentGridSize value) =>
        Enum.IsDefined(value) ? value : EnvironmentGridSize.Medium;
}

using System;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using Godot;

namespace DesktopBuddy.Platform;

public partial class DesktopShellController
{
    public async Task SaveWorkPreferencesAsync(
        Rect2I rect,
        bool positionSet,
        bool animationsEnabled,
        bool showLifetimeCounter)
    {
        if (_saves is null)
            return;

        _settings = _settings with
        {
            Revision = _settings.Revision == long.MaxValue ? long.MaxValue : _settings.Revision + 1,
            WorkWindowX = rect.Position.X,
            WorkWindowY = rect.Position.Y,
            WorkWindowWidth = rect.Size.X,
            WorkWindowHeight = rect.Size.Y,
            WorkPositionSet = positionSet,
            WorkAnimationsEnabled = animationsEnabled,
            WorkShowLifetimeCounter = showLifetimeCounter,
        };
        _saves.RegisterSettings(_settings);
        await _saves.SaveRegisteredSettingsAsync();
    }

    /// <summary>
    /// Forgets the player's Work placement: the companion is written back centred at its
    /// default size, and moved there now if Work Mode happens to be running.
    /// </summary>
    public async Task ResetWorkPlacementAsync(Vector2I size)
    {
        Rect2I centred = Window.CentredWorkCompanionRect(size);
        if (Window.WorkCompanionActive)
        {
            Window.ResizeWorkCompanion(centred.Size);
            Window.MoveWorkCompanion(centred.Position);
        }

        await SaveWorkPreferencesAsync(
            centred,
            positionSet: true,
            _settings.WorkAnimationsEnabled,
            _settings.WorkShowLifetimeCounter);
    }

    public Rect2I ResolveInitialWorkCompanionRect(Vector2I size)
    {
        if (_settings.WorkWindowWidth > 0 && _settings.WorkWindowHeight > 0)
            size = new Vector2I(_settings.WorkWindowWidth, _settings.WorkWindowHeight);

        if (_settings.WorkPositionSet)
        {
            return Window.RecoverWorkCompanionRect(new Rect2I(
                new Vector2I(_settings.WorkWindowX, _settings.WorkWindowY),
                size));
        }

        return Window.DefaultWorkCompanionRect(size);
    }
}

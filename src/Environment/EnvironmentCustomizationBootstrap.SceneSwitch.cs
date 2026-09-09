using System;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.Environment;

public partial class EnvironmentCustomizationBootstrap
{
    /// <summary>
    /// Scene switching must resolve modal/edit state before persistence or runtime teardown. An open
    /// room workspace remains pinned to the Scene it opened against and therefore blocks switching
    /// until the player closes/commits/cancels it.
    /// </summary>
    internal bool HasOpenSceneEditor =>
        (_decorator is not null && _decorator.IsOpen) ||
        (_backgroundEditor is not null && _backgroundEditor.IsOpen);

    /// <summary>
    /// Applies the already-existing Scene environment rebind immediately inside the ordered Scene
    /// switch transaction. If this bootstrap has not composed yet there is nothing live to rebind;
    /// its normal first composition will read the coordinator's then-current active Scene.
    /// </summary>
    internal bool TryRebindActiveSceneNow(SceneProgressCoordinator scenes)
    {
        ArgumentNullException.ThrowIfNull(scenes);
        if (_registration is null)
            return true;
        if (HasOpenSceneEditor)
            return false;
        if (_composedSceneId == scenes.ActiveSceneId)
            return true;

        RefreshSceneEnvironment(scenes);
        return _composedSceneId == scenes.ActiveSceneId;
    }
}

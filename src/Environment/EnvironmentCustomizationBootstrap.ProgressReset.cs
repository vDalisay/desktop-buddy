using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.Environment;

public partial class EnvironmentCustomizationBootstrap
{
    /// <summary>
    /// Reset Progress can replace the active Home Scene document while retaining Home's stable
    /// Scene ID. The ordinary Scene-switch watcher therefore has no ID change to observe. Adopt the
    /// freshly committed Environment snapshot into the existing presentation state so decoration
    /// visuals and future editor sessions immediately reflect the reset generation.
    /// </summary>
    public void RefreshAfterProgressReset()
    {
        if (_sandbox?.SceneProgress is not SceneProgressCoordinator scenes || _environmentState is null)
            return;

        EnvironmentProgressSnapshot snapshot = scenes.ActiveEnvironmentProgress;
        if (_composedSceneId == scenes.ActiveSceneId)
        {
            _environmentState.Adopt(snapshot);
            return;
        }

        // Defensive fallback for a future reset policy that chooses another active Scene ID.
        RefreshSceneEnvironment(scenes);
    }
}

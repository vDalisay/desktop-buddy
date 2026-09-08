using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    private bool _sceneResetHandlerInstalled;

    /// <summary>
    /// The legacy SandboxRoot owns the original Reset Progress event subscription in its established
    /// Initial-Demo path. Once _Ready has composed TrayCommands, Scene builds replace only that one
    /// handler with the split-state transaction. This staged seam keeps the Initial Demo behavior
    /// byte-for-byte isolated while Scene production composition is being introduced.
    /// </summary>
    public override void _Process(double delta)
    {
        if (_sceneResetHandlerInstalled || !GodotObject.IsInstanceValid(TrayCommands))
            return;

        _sceneResetHandlerInstalled = true;
        if (SceneProgress is not null)
        {
            TrayCommands.ResetProgressConfirmed -= OnResetProgressConfirmed;
            TrayCommands.ResetProgressConfirmed += OnSceneResetProgressConfirmed;
        }

        // SandboxRoot has no other idle-loop work; gameplay remains on the one fixed-tick route.
        SetProcess(false);
    }

    private async void OnSceneResetProgressConfirmed()
    {
        SceneProgressCoordinator? scenes = SceneProgress;
        if (scenes is null)
        {
            // Defensive only: the handler is installed exclusively for split-state runs.
            OnResetProgressConfirmed();
            return;
        }

        CharacterStore? characters = _runContext?.Characters;
        bool reset = await ProgressReset.ResetSceneAsync(
            scenes,
            Economy,
            deleteCharacters: characters is null
                ? null
                : token => characters.DeleteAllAsync(token));
        if (!reset)
        {
            Log.Error("Persistence", "Scene Reset Progress failed to commit; progress is unchanged.");
            return;
        }

        // Keep the read-only legacy aggregate projection coherent for staged presentation consumers.
        // It remains non-authoritative and cannot write through the Scene build's compatibility store.
        if (scenes.TryGetBuddy(
                BuddyIdentityId.LegacyPrimary,
                out BuddyIdentityState? primaryBuddy) &&
            primaryBuddy is not null)
        {
            LegacyProgressAggregateSnapshot compatibility =
                LegacyProgressPartitionPolicy.RecombineForLegacy(
                    new LegacyProgressPartition(
                        scenes.Player.Snapshot(),
                        primaryBuddy.Snapshot()));
            Progress.Adopt(compatibility.Progress);
        }

#if !DESKTOP_BUDDY_PUBLIC_WEB
        // Reset Progress wipes the Scene-owned room and its local painted background together.
        if (GetTree().Root.FindChild(
                nameof(DesktopBuddy.Environment.EnvironmentCustomizationBootstrap),
                true,
                false)
            is DesktopBuddy.Environment.EnvironmentCustomizationBootstrap environment)
        {
            environment.ClearPaintedBackground();
            environment.RefreshAfterProgressReset();
        }
#endif

        // The durable reset removed custom Character selection from the primary Buddy. Keep the
        // presentation coordinator on the same built-in choice without creating a competing save.
        if (GetTree().Root.FindChild(nameof(CharacterSelectionRuntime), true, false)
            is CharacterSelectionRuntime runtime && runtime.Coordinator is not null)
        {
            runtime.Coordinator.RevertToBuiltIn();
        }

        // Reset replaces the Home Scene document instance. Recompose the active host so its Scene
        // and split progress registry point at that committed document rather than the old object.
        InitializeSceneRuntimeHostIfEnabled();
        OnSessionResumed();
        Buddy.Recovery.ResetForSessionResume();
        Log.Info(
            "Persistence",
            $"Scene progress reset to a first run; {ProgressReset.DeletedCharacterCount} character(s) " +
            "removed, settings untouched.");
    }
}

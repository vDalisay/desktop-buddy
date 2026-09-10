using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Scenes;

/// <summary>
/// Duplicate and delete for the Scene library. Document state is owned by
/// <see cref="SceneProgressCoordinator"/>; this adds the player controls and the Scene-owned room
/// asset copy/cleanup that only the engine side can perform.
/// </summary>
public partial class SceneStripController
{
    private ConfirmationDialog? _deleteDialog;

    private static string SaveRoot => ProjectSettings.GlobalizePath("user://");

    private async void DuplicateActiveSceneMenuAsync() => await DuplicateActiveSceneAsync();

    /// <summary>
    /// Copies the active Scene to a new adjacent Scene: copied room state and Buddy references, new
    /// Scene and placement IDs, and an independent copy of the painted background so editing one
    /// room cannot change the other. Returns the new Scene, or an invalid ID when nothing was added.
    /// </summary>
    public async Task<SceneId> DuplicateActiveSceneAsync()
    {
        if (!CanMutateScenes(out string blocked))
        {
            SetStatus(blocked);
            return default;
        }
        if (!_scenes.CanCreateScene)
        {
            SetStatus("The Scene limit has been reached.");
            return default;
        }

        SceneId sourceId = _scenes.ActiveSceneId;
        string sourceName = _scenes.ActiveScene.Name;
        SceneLibraryResult duplicated = _scenes.DuplicateScene(sourceId);
        if (!duplicated.Succeeded || duplicated.Scene is not { } copy)
        {
            SetStatus($"Could not duplicate the Scene ({duplicated.Status}).");
            return default;
        }

        Rebuild();
        try
        {
            await CopySceneBackgroundAsync(sourceId, copy.SceneId);
            await _scenes.FlushAsync(force: true);
            SetStatus($"Duplicated {sourceName} as {copy.Name}.");
        }
        catch (Exception exception)
        {
            Diagnostics.Log.Error("SceneLibrary", $"Scene duplication follow-up failed: {exception}");
            SetStatus($"Duplicated {sourceName}, but its room could not be fully copied: {exception.Message}");
        }
        return copy.SceneId;
    }

    private void ConfirmDeleteActiveScene()
    {
        if (!CanMutateScenes(out string blocked))
        {
            SetStatus(blocked);
            return;
        }
        if (_scenes.SceneCount <= 1)
        {
            SetStatus("The last Scene cannot be deleted.");
            return;
        }

        _deleteDialog ??= BuildDeleteDialog();
        _deleteDialog.DialogText =
            $"Delete {_scenes.ActiveScene.Name}? Its room and layout are removed. Buddies stay in your library.";
        _deleteDialog.PopupCentered();
    }

    private ConfirmationDialog BuildDeleteDialog()
    {
        var dialog = new ConfirmationDialog
        {
            Name = "SceneDeleteDialog",
            Title = "Delete Scene",
            OkButtonText = "Delete",
            MinSize = new Vector2I(380, 160),
            Theme = Win98ThemeFactory.Create(),
        };
        dialog.Confirmed += async () => await DeleteActiveSceneAsync();
        AddChild(dialog);
        return dialog;
    }

    /// <summary>
    /// Deletes the active Scene. The runtime moves to the adjacent surviving Scene through the
    /// normal switch transaction first, so the deletion never runs against a live room, and the
    /// Scene's own room assets are removed only after the deletion has committed.
    /// </summary>
    public async Task<bool> DeleteActiveSceneAsync()
    {
        if (!CanMutateScenes(out string blocked))
        {
            SetStatus(blocked);
            return false;
        }
        if (_scenes.SceneCount <= 1)
        {
            SetStatus("The last Scene cannot be deleted.");
            return false;
        }

        SceneId doomedId = _scenes.ActiveSceneId;
        string doomedName = _scenes.ActiveScene.Name;
        SceneId successorId = AdjacentSceneId(doomedId);
        _castBusy = true;
        Rebuild();
        try
        {
            SceneRuntimeSwitchResult switched = await _sandbox.SwitchSceneAsync(successorId);
            if (!switched.Succeeded)
            {
                SetStatus(switched.Detail ?? $"Could not leave {doomedName} ({switched.Status}).");
                return false;
            }

            SceneLibraryResult deleted = _scenes.DeleteScene(doomedId);
            if (!deleted.Succeeded)
            {
                SetStatus($"Could not delete the Scene ({deleted.Status}).");
                return false;
            }

            await _scenes.FlushAsync(force: true);
            DeleteSceneAssets(doomedId);
            SetStatus($"Deleted {doomedName}.");
            return true;
        }
        catch (Exception exception)
        {
            Diagnostics.Log.Error("SceneLibrary", $"Scene deletion failed: {exception}");
            SetStatus($"Could not delete the Scene: {exception.Message}");
            return false;
        }
        finally
        {
            _castBusy = false;
            Rebuild();
        }
    }

    private SceneId AdjacentSceneId(SceneId sceneId)
    {
        System.Collections.Generic.IReadOnlyList<SceneDocument> scenes = _scenes.Scenes;
        for (int index = 0; index < scenes.Count; index++)
        {
            if (scenes[index].SceneId != sceneId)
                continue;
            return index + 1 < scenes.Count ? scenes[index + 1].SceneId : scenes[index - 1].SceneId;
        }
        throw new InvalidOperationException("The Scene to delete is not in the library.");
    }

    private static async Task CopySceneBackgroundAsync(SceneId sourceId, SceneId targetId)
    {
        var files = new CharacterFileSystem();
        byte[]? painted = EnvironmentPaintStore.ForScene(files, SaveRoot, sourceId).Load();
        if (painted is null)
            return;
        await EnvironmentPaintStore.ForScene(files, SaveRoot, targetId)
            .SaveAsync(painted, CancellationToken.None);
    }

    private static void DeleteSceneAssets(SceneId sceneId)
    {
        // Committed generations are addressed by the manifest, so a leftover directory would only
        // be dead bytes the player believes they deleted.
        try
        {
            var files = new CharacterFileSystem();
            string path = Path.Combine(SaveRoot, SceneStoragePaths.SceneRoot(sceneId).Replace('/', Path.DirectorySeparatorChar));
            if (files.DirectoryExists(path))
                files.DeleteDirectory(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log.Error("SceneLibrary", $"Deleted Scene {sceneId} left files behind: {exception.Message}");
        }
    }
}

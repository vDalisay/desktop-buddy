using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Scenes;

/// <summary>
/// The Scene workspace: one window holding every Scene and cast control, opened by a single click
/// on the Scene button.
///
/// <para>Scene management used to be a <see cref="MenuButton"/> dropdown beside three tabs, which
/// put switching, renaming, the cast and the overflow list in four different places and read as a
/// bare dropdown rather than as part of the shell (owner instruction 2026-09-10). One button, one
/// window, everything inside it.</para>
/// </summary>
public partial class SceneStripController
{
    private Control? _menuBlocker;
    private ItemList? _menuScenes;
    private ItemList? _menuCast;
    private readonly List<SceneId> _menuSceneIds = [];

    private bool MenuIsOpen =>
        _menuBlocker is not null && GodotObject.IsInstanceValid(_menuBlocker) && _menuBlocker.Visible;

    private void OpenSceneMenu()
    {
        _menuBlocker = OpenShellModal(
            "SceneWorkspaceDialog",
            "Scenes",
            new Vector2(420, 452),
            out VBoxContainer body,
            out Label message);
        if (_menuBlocker is null)
            return;

        message.Text = $"Active Scene: {_scenes.ActiveScene.Name}";

        var scenesGroup = new Win98GroupBox { Name = "SceneWorkspaceScenes" };
        scenesGroup.Configure("Scenes");
        body.AddChild(scenesGroup);
        _menuScenes = new ItemList
        {
            Name = "SceneWorkspaceSceneList",
            CustomMinimumSize = new Vector2(380, Win98ThemeFactory.Px(96)),
        };
        _menuScenes.ItemActivated += index => SwitchToSelectedScene((int)index);
        scenesGroup.Content.AddChild(_menuScenes);

        var sceneActions = new HBoxContainer { Name = "SceneWorkspaceSceneActions" };
        sceneActions.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        scenesGroup.Content.AddChild(sceneActions);
        Win98Dialog.Action(sceneActions, "Switch", () =>
        {
            int[] selected = _menuScenes!.GetSelectedItems();
            if (selected.Length > 0)
                SwitchToSelectedScene(selected[0]);
        }).Name = "SceneWorkspaceSwitchButton";
        Win98Dialog.Action(sceneActions, "New...", () => OpenNameDialog(rename: false)).Name =
            "SceneCreateButton";
        Win98Dialog.Action(sceneActions, "Rename...", () => OpenNameDialog(rename: true)).Name =
            "SceneWorkspaceRenameButton";

        var sceneActions2 = new HBoxContainer { Name = "SceneWorkspaceSceneActions2" };
        sceneActions2.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        scenesGroup.Content.AddChild(sceneActions2);
        Win98Dialog.Action(sceneActions2, "Duplicate", DuplicateActiveSceneMenuAsync).Name =
            "SceneWorkspaceDuplicateButton";
        Win98Dialog.Action(sceneActions2, "Delete...", ConfirmDeleteActiveScene).Name =
            "SceneWorkspaceDeleteButton";

        var castGroup = new Win98GroupBox { Name = "SceneWorkspaceCast" };
        castGroup.Configure("Cast in this Scene");
        body.AddChild(castGroup);
        _menuCast = new ItemList
        {
            Name = "SceneWorkspaceCastList",
            CustomMinimumSize = new Vector2(380, Win98ThemeFactory.Px(80)),
        };
        _menuCast.ItemActivated += index => FocusSelectedCastMember((int)index);
        castGroup.Content.AddChild(_menuCast);

        var castActions = new HBoxContainer { Name = "SceneWorkspaceCastActions" };
        castActions.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        castGroup.Content.AddChild(castActions);
        Win98Dialog.Action(castActions, "Focus", () =>
        {
            int[] selected = _menuCast!.GetSelectedItems();
            if (selected.Length > 0)
                FocusSelectedCastMember(selected[0]);
        }).Name = "SceneWorkspaceFocusButton";
        Win98Dialog.Action(castActions, "Add Buddy...", OpenAddBuddyPicker).Name =
            "SceneWorkspaceAddBuddyButton";
        Win98Dialog.Action(castActions, "Remove Focused", RemoveFocusedCastMemberAsync).Name =
            "SceneWorkspaceRemoveBuddyButton";

        HBoxContainer close = ModalActions(body, "SceneWorkspaceActions");
        Win98Dialog.Action(close, "Close", () => _menuBlocker!.Visible = false).Name =
            "SceneWorkspaceCloseButton";

        RefreshSceneMenu();
    }

    /// <summary>
    /// Repopulates the open workspace. Every control here acts on the live Scene library, so the
    /// lists are rebuilt from it rather than cached at open time.
    /// </summary>
    private void RefreshSceneMenu()
    {
        if (!MenuIsOpen || _menuScenes is null || _menuCast is null)
            return;

        bool busy = _sandbox.IsSceneSwitchInProgress;
        _menuScenes.Clear();
        _menuSceneIds.Clear();
        IReadOnlyList<SceneDocument> scenes = _scenes.Scenes;
        int activeIndex = 0;
        for (int index = 0; index < scenes.Count; index++)
        {
            SceneDocument scene = scenes[index];
            bool isActive = scene.SceneId == _scenes.ActiveSceneId;
            _menuScenes.AddItem($"{(isActive ? "• " : string.Empty)}{scene.Name}");
            _menuSceneIds.Add(scene.SceneId);
            if (isActive)
                activeIndex = index;
        }
        if (scenes.Count > 0)
            _menuScenes.Select(activeIndex);

        _menuCast.Clear();
        IReadOnlyList<BuddyPlacement> placements = _scenes.ActiveScene.BuddyPlacements;
        BuddyPlacementId focused = _sandbox.FocusedActor?.PlacementId ?? default;
        for (int index = 0; index < placements.Count; index++)
        {
            BuddyPlacement placement = placements[index];
            bool isFocused = placement.PlacementId == focused;
            _menuCast.AddItem(
                $"{(isFocused ? "• " : string.Empty)}{CastLabel(index, placement.BuddyIdentityId)}");
        }
        if (placements.Count == 0)
        {
            _menuCast.AddItem("This room is empty.");
            _menuCast.SetItemDisabled(0, true);
        }

        SetMenuButtonDisabled("SceneWorkspaceSwitchButton", busy || scenes.Count <= 1);
        SetMenuButtonDisabled("SceneCreateButton", busy || !_scenes.CanCreateScene);
        SetMenuButtonDisabled("SceneWorkspaceRenameButton", busy);
        SetMenuButtonDisabled("SceneWorkspaceDuplicateButton", busy || !_scenes.CanCreateScene);
        SetMenuButtonDisabled("SceneWorkspaceDeleteButton", busy || scenes.Count <= 1);
        SetMenuButtonDisabled("SceneWorkspaceFocusButton", busy || placements.Count == 0);
        SetMenuButtonDisabled("SceneWorkspaceAddBuddyButton", busy || _castBusy);
        SetMenuButtonDisabled(
            "SceneWorkspaceRemoveBuddyButton", busy || _castBusy || _sandbox.FocusedActor is null);
    }

    private void SetMenuButtonDisabled(string name, bool disabled)
    {
        if (_menuBlocker?.FindChild(name, recursive: true, owned: false) is Button button)
            button.Disabled = disabled;
    }

    private void SwitchToSelectedScene(int index)
    {
        if (index < 0 || index >= _menuSceneIds.Count)
            return;
        SwitchToAsync(_menuSceneIds[index]);
    }

    private void FocusSelectedCastMember(int index)
    {
        if (index < 0 || index >= _scenes.ActiveScene.BuddyPlacements.Count)
            return;
        FocusCastMember(index);
    }
}

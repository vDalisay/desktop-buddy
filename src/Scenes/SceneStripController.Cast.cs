using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Scenes;

/// <summary>
/// Cast commands for the active Scene. Scene documents, Buddy identities and runtime composition
/// keep their existing owners; this only turns them into player controls.
/// </summary>
public partial class SceneStripController
{
    private const long FocusBuddyItemBase = 300;

    private readonly List<CastChoice> _choices = [];
    private readonly Dictionary<Guid, string> _characterNames = [];
    private Control? _pickerBlocker;
    private ItemList? _pickerList;
    private CanvasLayer? _previewLayer;
    private Label? _previewLabel;
    private const float GhostOpacity = 0.55f;
    private const float GhostPadding = 16.0f;

    private CastChoice? _placing;
    private bool _restoreMenuAfterPlacement;
    private SubViewportContainer? _ghost;
    private BuddyPreviewSurface? _ghostSurface;
    private RuntimePaintTextureBridge? _ghostPaint;
    private int _ghostSide;
    private int _ghostRequest;
    private bool _castBusy;

    /// <summary>An existing Buddy identity to reuse, or a Character to register a new Buddy from.</summary>
    private readonly record struct CastChoice(BuddyIdentityId Identity, Guid? CharacterId, string Label);

    private void AppendCastMenuItems(PopupMenu popup)
    {
        IReadOnlyList<BuddyPlacement> placements = _scenes.ActiveScene.BuddyPlacements;
        BuddyPlacementId focused = _sandbox.FocusedActor?.PlacementId ?? default;
        bool busy = _castBusy || _sandbox.IsSceneSwitchInProgress;

        for (int index = 0; index < placements.Count; index++)
        {
            BuddyPlacement placement = placements[index];
            bool isFocused = placement.PlacementId == focused;
            popup.AddItem(
                $"{(isFocused ? "• " : string.Empty)}{CastLabel(index, placement.BuddyIdentityId)}",
                (int)(FocusBuddyItemBase + index));
            popup.SetItemDisabled(popup.ItemCount - 1, busy || isFocused);
        }

        popup.AddItem("Remove Focused Buddy", 5);
        popup.SetItemDisabled(popup.ItemCount - 1, busy || _sandbox.FocusedActor is null);
    }

    private void FocusCastMember(int placementIndex)
    {
        IReadOnlyList<BuddyPlacement> placements = _scenes.ActiveScene.BuddyPlacements;
        if (placementIndex < 0 || placementIndex >= placements.Count)
            return;
        if (!_sandbox.TryFocusActor(placements[placementIndex].PlacementId))
        {
            SetStatus("That Buddy is not in the room right now.");
            return;
        }
        Rebuild();
        SetStatus($"Focused {CastLabel(placementIndex, placements[placementIndex].BuddyIdentityId)}.");
    }

    private async void RemoveFocusedCastMemberAsync()
    {
        if (_sandbox.FocusedActor is not { } focused)
        {
            SetStatus("No Buddy is focused.");
            return;
        }
        await RemoveCastMemberAsync(focused.BuddyIdentityId);
    }

    private string CastLabel(int index, BuddyIdentityId identity)
    {
        string name = "Buddy";
        if (_scenes.TryGetBuddy(identity, out BuddyIdentityState? buddy) && buddy?.CharacterId is { } characterId &&
            _characterNames.TryGetValue(characterId, out string? stored))
        {
            name = stored;
        }
        return $"{index + 1}. {name}";
    }

    private async void OpenAddBuddyPicker()
    {
        if (!CanMutateScenes(out string blocked))
        {
            SetStatus(blocked);
            return;
        }

        await RefreshCharacterNamesAsync();
        _choices.Clear();
        var placed = new HashSet<BuddyIdentityId>();
        foreach (BuddyPlacement placement in _scenes.ActiveScene.BuddyPlacements)
            placed.Add(placement.BuddyIdentityId);

        foreach (BuddyIdentityState buddy in _scenes.BuddyIdentities())
        {
            if (placed.Contains(buddy.BuddyIdentityId))
                continue;
            string name = buddy.CharacterId is { } id && _characterNames.TryGetValue(id, out string? stored)
                ? stored
                : "Buddy";
            _choices.Add(new CastChoice(buddy.BuddyIdentityId, buddy.CharacterId, $"{name} (existing Buddy)"));
        }
        foreach ((Guid characterId, string name) in _characterNames)
            _choices.Add(new CastChoice(default, characterId, $"{name} (new Buddy)"));

        _pickerBlocker = OpenShellModal(
            "SceneAddBuddyDialog",
            "Add Buddy",
            new Vector2(400, 360),
            out VBoxContainer body,
            out Label message);
        if (_pickerBlocker is null)
            return;

        message.Text = "Choose a Buddy or Character, then click in the room to place it.";
        _pickerList = new ItemList
        {
            Name = "SceneAddBuddyList",
            CustomMinimumSize = new Vector2(340, Win98ThemeFactory.Px(180)),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        body.AddChild(_pickerList);
        foreach (CastChoice choice in _choices)
            _pickerList.AddItem(choice.Label);
        if (_choices.Count == 0)
        {
            _pickerList.AddItem("No Buddy or Character is available to add.");
            _pickerList.SetItemDisabled(0, true);
        }
        else
        {
            _pickerList.Select(0);
        }
        _pickerList.ItemActivated += _ =>
        {
            _pickerBlocker!.Visible = false;
            BeginPlacementFromPicker();
        };

        HBoxContainer actions = ModalActions(body, "SceneAddBuddyActions");
        Win98Dialog.Action(actions, "Choose", () =>
        {
            _pickerBlocker!.Visible = false;
            BeginPlacementFromPicker();
        }).Name = "SceneAddBuddyChooseButton";
        Win98Dialog.Action(actions, "Cancel", () => _pickerBlocker!.Visible = false).Name =
            "SceneAddBuddyCancelButton";
    }

    private void BeginPlacementFromPicker()
    {
        int[] selected = _pickerList!.GetSelectedItems();
        if (selected.Length == 0 || selected[0] >= _choices.Count)
            return;

        _placing = _choices[selected[0]];
        // The Scene workspace covers the room; it steps aside while the player aims.
        if (MenuIsOpen)
        {
            _menuBlocker!.Visible = false;
            _restoreMenuAfterPlacement = true;
        }
        ShowPlacementPreview(_placing.Value);
        SetStatus($"Click in the room to place {_placing.Value.Label}. Escape cancels.");
    }

    public override void _Input(InputEvent @event)
    {
        if (_placing is null)
            return;

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape } or
            InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right })
        {
            CancelPlacement("Placement cancelled.");
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return;

        GetViewport().SetInputAsHandled();
        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        Vector2 world = _sandbox.GetGlobalMousePosition();
        if (!bounds.HasPoint(world))
        {
            SetStatus("Click inside the room to place the Buddy.");
            return;
        }

        CastChoice choice = _placing.Value;
        CancelPlacement(null);
        AddCastMemberAsync(
            choice,
            new CanonicalRoomPosition(
                Mathf.Clamp((world.X - bounds.Position.X) / Math.Max(1.0f, bounds.Size.X), 0.0f, 1.0f),
                Mathf.Clamp((world.Y - bounds.Position.Y) / Math.Max(1.0f, bounds.Size.Y), 0.0f, 1.0f)));
    }

    private async void AddCastMemberAsync(CastChoice choice, CanonicalRoomPosition position) =>
        await AddCastMemberAsync(choice.Identity, choice.CharacterId, choice.Label, position);

    /// <summary>
    /// Adds one Buddy to the active Scene, registering a fresh identity with authored defaults when
    /// the player picked a Character that has none yet. An invalid identity means "new Buddy".
    /// Returns the placed Buddy identity, or an invalid identity when nothing was added.
    /// </summary>
    public async Task<BuddyIdentityId> AddCastMemberAsync(
        BuddyIdentityId requestedIdentity,
        Guid? characterId,
        string label,
        CanonicalRoomPosition position)
    {
        var choice = new CastChoice(requestedIdentity, characterId, label);
        if (!CanMutateScenes(out string blocked))
        {
            SetStatus(blocked);
            return default;
        }

        BuddyIdentityId identity = choice.Identity;
        bool registered = false;
        if (!identity.IsValid)
        {
            identity = BuddyIdentityId.From(Guid.NewGuid());
            var created = new BuddyIdentityState(new BuddyIdentitySnapshot(
                identity,
                Revision: 0,
                choice.CharacterId,
                Mood: 0.0f,
                Fullness: 0.0f,
                HarmfulContentIds: [],
                BuddyTraits.Default));
            if (!_scenes.RegisterBuddyIdentity(created))
            {
                SetStatus("Could not register the new Buddy identity.");
                return default;
            }
            registered = true;
        }

        SceneLibraryResult added = _scenes.AddBuddyToScene(_scenes.ActiveSceneId, identity, position);
        if (!added.Succeeded)
        {
            if (registered)
                _scenes.RemoveBuddyIdentity(identity);
            SetStatus(added.Status == SceneLibraryStatus.BuddyAlreadyPresent
                ? "That Buddy is already in this Scene."
                : $"Could not add the Buddy ({added.Status}).");
            return default;
        }

        await ApplyCastChangeAsync($"Added {choice.Label} to {_scenes.ActiveScene.Name}.");
        return identity;
    }

    /// <summary>
    /// Removes one placement from the active Scene. The Buddy identity, its Character and its paint
    /// files stay in the local library so the same Buddy can be added again or used elsewhere.
    /// </summary>
    public async Task<bool> RemoveCastMemberAsync(BuddyIdentityId identity)
    {
        if (!CanMutateScenes(out string blocked))
        {
            SetStatus(blocked);
            return false;
        }

        SceneLibraryResult removed = _scenes.RemoveBuddyFromScene(_scenes.ActiveSceneId, identity);
        if (!removed.Succeeded)
        {
            SetStatus($"Could not remove the Buddy ({removed.Status}).");
            return false;
        }

        await ApplyCastChangeAsync($"Removed a Buddy from {_scenes.ActiveScene.Name}.");
        return true;
    }

    /// <summary>Recomposes and commits the active Scene after its cast document changed.</summary>
    private async Task ApplyCastChangeAsync(string successStatus)
    {
        _castBusy = true;
        Rebuild();
        try
        {
            SceneRuntimeSwitchResult result = await _sandbox.ReloadActiveSceneAsync();
            if (result.Succeeded)
            {
                SetStatus(successStatus);
            }
            else
            {
                string detail = result.Detail ?? $"Could not update the Scene cast ({result.Status}).";
                Diagnostics.Log.Error("SceneCast", $"Cast recomposition failed ({result.Status}): {detail}");
                SetStatus(detail);
            }
        }
        catch (Exception exception)
        {
            Diagnostics.Log.Error("SceneCast", $"Cast recomposition threw: {exception}");
            SetStatus($"Could not update the Scene cast: {exception.Message}");
        }
        finally
        {
            _castBusy = false;
            Rebuild();
        }
    }

    private bool CanMutateScenes(out string blocked)
    {
        if (_castBusy || _sandbox.IsSceneSwitchInProgress)
        {
            blocked = "The Scene is still busy.";
            return false;
        }
        if (_sandbox.Shell.Mode != InputMode.Play || _sandbox.Lifecycle.IsEditorModeActive)
        {
            blocked = "Close Work/Edit state before changing this Scene.";
            return false;
        }
        blocked = string.Empty;
        return true;
    }

    private async Task RefreshCharacterNamesAsync()
    {
        // Scene builds are native desktop builds; the browser scope has no Scene surface at all.
        if (_sandbox.Characters is not { } characters)
            return;

        try
        {
            var library = new CharacterLibraryIndex(new CharacterFileSystem(), characters.Paths.Root);
            IReadOnlyList<CharacterIndexEntry> page = await library.ReadPageAsync(0, 64, CancellationToken.None);
            _characterNames.Clear();
            foreach (CharacterIndexEntry entry in page)
            {
                if (entry.IsEnabled)
                    _characterNames[entry.CharacterId] = entry.DisplayName;
            }
        }
        catch (Exception exception)
        {
            SetStatus($"Could not read the Character library: {exception.Message}");
        }
    }

    /// <summary>
    /// Shows a translucent ghost of the Buddy being placed, dressed as the Character that will
    /// appear, standing exactly where the click will put it (owner instruction 2026-09-10). The
    /// ghost renders once into an offscreen <see cref="BuddyPreviewSurface"/> at 1 px per world
    /// unit, so it is the real Buddy's size, and follows the same spawn clamp as the placement.
    /// </summary>
    private void ShowPlacementPreview(CastChoice choice)
    {
        if (_previewLayer is null)
        {
            _previewLayer = new CanvasLayer { Name = "ScenePlacementPreview", Layer = 100 };
            AddChild(_previewLayer);
            BuildPlacementGhost();
            _previewLabel = new Label
            {
                Name = "ScenePlacementPreviewLabel",
                Theme = Win98ThemeFactory.Create(),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _previewLayer.AddChild(_previewLabel);
        }
        _previewLabel!.Text = $"Place {choice.Label}";
        _previewLayer.Visible = true;
        DressPlacementGhostAsync(choice.CharacterId);
        UpdatePlacementPreview();
    }

    private void BuildPlacementGhost()
    {
        PuppetRigProfile rig = _sandbox.Buddy.Rig.Profile;
        float extent = 0.0f;
        foreach (PuppetPartDefinition part in rig.Parts)
        {
            extent = Math.Max(extent,
                Math.Max(Math.Abs(part.RestPosition.X), Math.Abs(part.RestPosition.Y)) + part.Radius);
        }
        _ghostSide = Mathf.CeilToInt(extent * 2.0f + GhostPadding);

        _ghost = new SubViewportContainer
        {
            Name = "ScenePlacementGhost",
            Stretch = true,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(_ghostSide, _ghostSide),
            Modulate = new Color(1.0f, 1.0f, 1.0f, GhostOpacity),
        };
        _previewLayer!.AddChild(_ghost);

        _ghostSurface = new BuddyPreviewSurface { Name = "ScenePlacementGhostViewport" };
        _ghostSurface.Configure(
            rigName: "ScenePlacementGhostRig",
            viewportSize: new Vector2I(_ghostSide, _ghostSide),
            transparentBackground: true,
            rigProfile: rig,
            visualProfile: _sandbox.Buddy.VisualProfile,
            // Orthographic height equal to the viewport height: one world unit per pixel, which
            // is the room's own scale, so the ghost is exactly the size of the Buddy it becomes.
            cameraSize: _ghostSide,
            cameraPosition: new Vector3(0, 0, 600),
            lightRotationDegrees: new Vector3(-30, -20, 0),
            lightEnergy: 0.9f,
            sourceOrigin: Vector2.Zero,
            face: ":)");
        _ghost.AddChild(_ghostSurface);
        _ghostPaint = new RuntimePaintTextureBridge(_ghostSurface.Rig);
    }

    private async void DressPlacementGhostAsync(Guid? characterId)
    {
        int request = ++_ghostRequest;
        CompiledCharacterAppearance appearance = BuiltInCharacterAppearance.Value;
        IReadOnlyDictionary<PaintPart, byte[]>? surfaces = null;
        if (characterId is { } id && _sandbox.Characters is { } characters)
        {
            try
            {
                CharacterPaintLoadResult loaded = await characters.CreatePaintStore().LoadAsync(id, CancellationToken.None);
                if (loaded.IsSuccess && loaded.Character.Document is { } document)
                {
                    CharacterCompileResult compiled = CharacterCompiler.Compile(document, characters.FeatureCatalog);
                    if (compiled.IsSuccess && compiled.Appearance is { } dressed)
                    {
                        appearance = dressed;
                        surfaces = loaded.Surfaces;
                    }
                }
            }
            catch (Exception exception)
            {
                // A ghost that cannot be dressed still shows where the Buddy lands.
                Diagnostics.Log.Warn("SceneCast", $"Placement ghost fell back to the built-in look: {exception.Message}");
            }
        }

        // A newer pick superseded this load, or placement ended while it ran.
        if (request != _ghostRequest || _placing is null || !GodotObject.IsInstanceValid(_ghostSurface))
            return;

        _ghostSurface!.Rig.ApplyAppearance(appearance);
        _ghostPaint!.Clear();
        if (surfaces is not null)
            _ghostPaint.Apply(surfaces);
        _ghostSurface.Rig.ApplyRestPose();
        _ghostSurface.RequestSingleFrame();
    }

    private void CancelPlacement(string? status)
    {
        _placing = null;
        _ghostRequest++;
        if (_previewLayer is not null)
            _previewLayer.Visible = false;
        // The Scene workspace stepped aside so the room could be seen; bring it back.
        if (_restoreMenuAfterPlacement && _menuBlocker is not null && GodotObject.IsInstanceValid(_menuBlocker))
        {
            _menuBlocker.Visible = true;
            RefreshSceneMenu();
        }
        _restoreMenuAfterPlacement = false;
        if (status is not null)
            SetStatus(status);
    }

    private void UpdatePlacementPreview()
    {
        if (_placing is null || _previewLabel is null)
            return;

        Vector2 mouse = GetViewport().GetMousePosition();
        _previewLabel.Position = mouse + new Vector2(12, 12);
        if (_ghost is null)
            return;

        // Room world -> screen through the canvas, so the ghost stands where the Buddy will.
        Transform2D canvas = GetViewport().CanvasTransform;
        Vector2 origin = _sandbox.PlannedSceneBuddyOrigin(_sandbox.GetGlobalMousePosition());
        Vector2 screen = canvas * origin;
        Vector2 scale = canvas.Scale;
        _ghost.Scale = scale;
        _ghost.Position = screen - new Vector2(_ghostSide, _ghostSide) * 0.5f * scale;
        _ghost.Visible = _sandbox.Boundaries.InnerBounds.HasPoint(_sandbox.GetGlobalMousePosition());
    }
}

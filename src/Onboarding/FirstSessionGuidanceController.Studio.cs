using DesktopBuddy.CharacterEditor.BuddyStudio;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// Every reference the walkthrough makes to the Buddy Studio workspace, in one place.
///
/// <para>Buddy Studio is compiled out of the reduced itch.io distribution, so the controller may
/// not name <see cref="BuddyStudioWorkspace"/> anywhere else — a build with the type absent has to
/// compile. The absent build gets <c>FirstSessionGuidanceController.StudioAbsent.cs</c> instead,
/// which answers the same questions with "there is no Studio".</para>
///
/// <para>These are the tutorial's own observations, not a Studio API. The Studio steps are already
/// excluded from the itch step sequence, so nothing here is reachable there either way; this seam
/// exists so the <em>type</em> can leave the assembly.</para>
/// </summary>
public sealed partial class FirstSessionGuidanceController
{
    private BuddyStudioWorkspace? _studio;
    private bool _studioSignalsBound;

    private void DiscoverStudio() =>
        _studio ??= GetTree().Root.FindChild(nameof(BuddyStudioWorkspace), true, false) as BuddyStudioWorkspace;

    private void BindStudioSignals()
    {
        if (_studioSignalsBound || !GodotObject.IsInstanceValid(_studio) || !_studio!.IsInsideTree())
            return;

        _studio.SaveAction.Pressed += OnStudioSavePressed;
        _studioSignalsBound = true;
    }

    private void UnbindStudioSignals()
    {
        if (_studioSignalsBound && GodotObject.IsInstanceValid(_studio))
            _studio!.SaveAction.Pressed -= OnStudioSavePressed;
    }

    private bool IsStudioOpen() =>
        GodotObject.IsInstanceValid(_studio) && _studio!.IsVisibleInTree();

    private bool IsStudioSlotSelected(CharacterFeatureSlot slot) =>
        IsStudioOpen() && _studio!.SelectedSlot == slot;

    private bool IsStudioCatalogSelection(string contentId) =>
        GodotObject.IsInstanceValid(_studio!.CatalogGrid) &&
        string.Equals(_studio.CatalogGrid.SelectedId, contentId, System.StringComparison.Ordinal);

    /// <summary>
    /// Whether Save has nothing left to write. Both the button state and the session's dirty flag
    /// are checked because "not dirty" alone is momentarily true while the workspace is still
    /// settling after an equip.
    /// </summary>
    private bool IsStudioSaveDisabled() =>
        GodotObject.IsInstanceValid(_studio) &&
        GodotObject.IsInstanceValid(_studio!.SaveAction) &&
        _studio.SaveAction.Disabled;

    private Control? StudioScope => _studio;

    private Control? StudioSaveAction => IsStudioOpen() ? _studio!.SaveAction : null;

    private Control? StudioCatalogGrid => IsStudioOpen() ? _studio!.CatalogGrid : null;

    private Control? StudioCatalogTile(string contentId) =>
        IsStudioOpen() ? _studio!.CatalogGrid.TileFor(contentId) : null;

    private Control? StudioCategoryButton(string categoryId) =>
        IsStudioOpen() ? _studio!.CategoryStrip.ButtonFor(categoryId) : null;
}

using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// The Buddy Studio seam for distributions that do not ship Buddy Studio.
///
/// <para>Compiled in place of <c>FirstSessionGuidanceController.Studio.cs</c> when the itch scope
/// removes the workspace source, so the controller compiles with no <c>BuddyStudioWorkspace</c>
/// type in the assembly at all. Every question answers "there is no Studio", which is the truth in
/// that build rather than a stub standing in for something hidden.</para>
///
/// <para>Nothing here is reachable in practice: the Studio steps are already absent from the itch
/// step sequence, so the walkthrough never asks. This exists to keep one controller compiling
/// against two different shipped surfaces.</para>
/// </summary>
public sealed partial class FirstSessionGuidanceController
{
    private void DiscoverStudio()
    {
    }

    private void BindStudioSignals()
    {
    }

    private void UnbindStudioSignals()
    {
    }

    private bool IsStudioOpen() => false;

    private bool IsStudioSlotSelected(CharacterFeatureSlot slot) => false;

    private bool IsStudioCatalogSelection(string contentId) => false;

    private bool IsStudioSaveDisabled() => false;

    private Control? StudioScope => null;

    private Control? StudioSaveAction => null;

    private Control? StudioCatalogGrid => null;

    private Control? StudioCatalogTile(string contentId) => null;

    private Control? StudioCategoryButton(string categoryId) => null;
}

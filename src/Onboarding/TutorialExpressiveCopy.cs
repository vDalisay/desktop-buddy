using System;
using DesktopBuddy.Domain.Presentation;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// English-only semantic emphasis pass for the current tutorial copy. The authoritative wording
/// remains in FirstSessionGuidanceController.TextFor; this layer only marks meaning for the
/// expressive renderer and fixes values that are genuinely runtime data, such as the Drop Tool
/// binding. When localization arrives these semantic markers can move into translated resources
/// without changing the presenter.
/// </summary>
internal static class TutorialExpressiveCopy
{
    public static string Format(string stepId, string? plainText, string dropToolBinding)
    {
        string text = plainText ?? string.Empty;
        return stepId switch
        {
            TutorialStepIds.GrabBuddy => Mark(text,
                ("left mouse button", ExpressiveSemanticRole.Input),
                ("grab", ExpressiveSemanticRole.Action)),
            TutorialStepIds.OpenInventory => Mark(text,
                ("Inventory", ExpressiveSemanticRole.Action)),
            TutorialStepIds.PurchaseBaseballBat => Mark(text,
                ("Credits", ExpressiveSemanticRole.Money),
                ("Baseball Bat", ExpressiveSemanticRole.Action)),
            TutorialStepIds.ChargedBatHit => Mark(text,
                ("Nice stuff!", ExpressiveSemanticRole.Playful),
                ("right mouse button", ExpressiveSemanticRole.Input),
                ("big swing", ExpressiveSemanticRole.Impact),
                ("charged swing", ExpressiveSemanticRole.Impact)),
            TutorialStepIds.UnequipTool => DropToolLine(dropToolBinding),
            TutorialStepIds.OpenPaintBuddy => Mark(text,
                ("Paint ▸ Buddy", ExpressiveSemanticRole.Action)),
            TutorialStepIds.CreateBuddy => Mark(text,
                ("+ New Character", ExpressiveSemanticRole.Action),
                ("Characters", ExpressiveSemanticRole.Action)),
            TutorialStepIds.SelectPaintBrush => Mark(text,
                ("Brush tool", ExpressiveSemanticRole.Action)),
            TutorialStepIds.SelectPaintColor => Mark(text,
                ("colour", ExpressiveSemanticRole.Action)),
            TutorialStepIds.PaintBuddy => Mark(text,
                ("Paint away!", ExpressiveSemanticRole.Playful),
                ("'Save'", ExpressiveSemanticRole.Action)),
            TutorialStepIds.UsePaintedBuddy => Mark(text,
                ("'Use Character'", ExpressiveSemanticRole.Action)),
            TutorialStepIds.AdmirePaintedBuddy => Tag(ExpressiveSemanticRole.Playful, text),
            TutorialStepIds.OpenPaintBackground => Mark(text,
                ("Paint ▸ Background", ExpressiveSemanticRole.Action)),
            TutorialStepIds.SelectBackgroundSpray => Mark(text,
                ("new but nostalgic", ExpressiveSemanticRole.Playful),
                ("Spray tool", ExpressiveSemanticRole.Action)),
            TutorialStepIds.SelectBackgroundColor => Mark(text,
                ("Palette", ExpressiveSemanticRole.Action)),
            TutorialStepIds.PaintBackground => Mark(text,
                ("Click and drag", ExpressiveSemanticRole.Input)),
            TutorialStepIds.FloatPaintBackgroundPanel => Mark(text,
                ("title bar", ExpressiveSemanticRole.Input),
                ("red pin button", ExpressiveSemanticRole.Input)),
            TutorialStepIds.SaveAndExitPaintBackground => Mark(text,
                ("Looks good!", ExpressiveSemanticRole.Playful),
                ("Save and Exit", ExpressiveSemanticRole.Action)),
            TutorialStepIds.OpenBuddyStudio => Mark(text,
                ("Buddy Studio", ExpressiveSemanticRole.Action)),
            TutorialStepIds.SelectNoseCategory => Mark(text,
                ("Nose category", ExpressiveSemanticRole.Action)),
            TutorialStepIds.SelectNoseButtonStyle => Mark(text,
                ("'Button nose'", ExpressiveSemanticRole.Playful)),
            TutorialStepIds.BuyStudioItem => Mark(text,
                ("It looks great!", ExpressiveSemanticRole.Playful),
                ("'Buy'", ExpressiveSemanticRole.Action),
                ("double-click", ExpressiveSemanticRole.Input)),
            TutorialStepIds.EquipStudioItem => Mark(text,
                ("equip it", ExpressiveSemanticRole.Action)),
            TutorialStepIds.SaveBuddyStudio => Mark(text,
                ("'Save'", ExpressiveSemanticRole.Action),
                ("beautiful nose", ExpressiveSemanticRole.Playful)),
            TutorialStepIds.AdmireStudioBuddy => Tag(ExpressiveSemanticRole.Playful, text),
            TutorialStepIds.EnterWorkMode => Mark(text,
                ("Work Mode", ExpressiveSemanticRole.Action)),
            TutorialStepIds.DragWorkCompanion => Mark(text,
                ("left mouse button", ExpressiveSemanticRole.Input),
                ("drag", ExpressiveSemanticRole.Action)),
            TutorialStepIds.ResizeWorkCompanion => Mark(text,
                ("resize button ↘", ExpressiveSemanticRole.Input),
                ("left mouse button", ExpressiveSemanticRole.Input),
                ("bigger or smaller", ExpressiveSemanticRole.Action)),
            TutorialStepIds.ToggleWorkCounter => Mark(text,
                ("earn money", ExpressiveSemanticRole.Money),
                ("Click on the screen", ExpressiveSemanticRole.Input)),
            TutorialStepIds.ExitWorkMode => Mark(text,
                ("Double-click", ExpressiveSemanticRole.Input),
                ("'X'", ExpressiveSemanticRole.Input),
                ("return", ExpressiveSemanticRole.Action)),
            TutorialStepIds.Farewell => Mark(text,
                ("best of buds!", ExpressiveSemanticRole.Playful),
                ("'?'", ExpressiveSemanticRole.Input),
                ("Have fun with your Buddy!", ExpressiveSemanticRole.Playful)),
            _ => text,
        };
    }

    private static string DropToolLine(string binding)
    {
        string shownBinding = string.IsNullOrWhiteSpace(binding) ? "D" : binding.Trim();
        return "To unequip a tool you can switch in the " +
               Tag(ExpressiveSemanticRole.Action, "Inventory") + " or press " +
               Tag(ExpressiveSemanticRole.Input, shownBinding) + " to drop it. You can re-equip " +
               "dropped tools by " + Tag(ExpressiveSemanticRole.Input, "double-clicking") +
               " them. Try to drop it now.";
    }

    private static string Mark(
        string source,
        params (string Text, ExpressiveSemanticRole Role)[] replacements)
    {
        string result = source;
        foreach ((string text, ExpressiveSemanticRole role) in replacements)
        {
            if (string.IsNullOrEmpty(text) || !result.Contains(text, StringComparison.Ordinal))
                continue;
            result = result.Replace(text, Tag(role, text), StringComparison.Ordinal);
        }
        return result;
    }

    private static string Tag(ExpressiveSemanticRole role, string text)
    {
        // '[' is the only syntax introducer in the deliberately tiny authoring language. A very
        // unusual custom key label containing it stays readable and simply skips emphasis.
        if (text.Contains('[', StringComparison.Ordinal))
            return text;
        string tag = ExpressiveSemanticTags.Name(role);
        return $"[{tag}]{text}[/{tag}]";
    }
}

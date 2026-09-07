using System;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// English authored tutorial copy for the lines that intentionally use semantic emphasis. The
/// semantic tags describe meaning rather than effects; the RichText presenter owns the visual
/// mapping. Conditional lines are not authored here and stay in the controller's own switch, so
/// there is no second progression or variant system hiding here.
///
/// <para>This is the single source of truth for every line it names: the controller's plain text
/// is the tag-stripped projection of these same strings rather than a second hand-maintained
/// copy. Two tables of the same prose drift silently — one already had before they were merged.</para>
/// </summary>
internal static class TutorialExpressiveCopy
{
    /// <summary>
    /// True when this step has authored emphasis. Steps that return false are owned entirely by
    /// the controller's conditional copy.
    /// </summary>
    public static bool TryFormat(string stepId, string dropToolBinding, out string semantic)
    {
        semantic = Format(stepId, dropToolBinding);
        return semantic.Length > 0;
    }

    /// <summary>Player-readable text for an authored line, with the semantic tags removed.</summary>
    public static string PlainTextFor(string stepId, string dropToolBinding) =>
        ExpressiveSemanticMarkup.PlainText(
            ExpressiveSemanticMarkup.Parse(Format(stepId, dropToolBinding)));

    private static string Format(string stepId, string dropToolBinding) => stepId switch
    {
        TutorialStepIds.GrabBuddy =>
            "Hi! Let me introduce you to your buddy. Click and hold your [input]left mouse button[/input] on " +
            "your Buddy to [action]grab[/action] him.",
        TutorialStepIds.OpenInventory =>
            "Now open the [action]Inventory[/action] in the top-left corner. This is where you can buy and equip " +
            "all sorts of different tools so you and your Buddy can play together.",
        TutorialStepIds.PurchaseBaseballBat =>
            "You can use your [money]Credits[/money] in the top-right to buy all kinds of things. You earn more " +
            "by playing with your Buddy. For now, let's buy and equip the [action]Baseball Bat[/action].",
        TutorialStepIds.ChargedBatHit =>
            "[playful]Nice stuff![/playful] You can hold the [input]right mouse button[/input] to charge your bat up for a " +
            "[impact]big swing[/impact]. Some other tools have some extra interaction by clicking or holding the " +
            "[input]right mouse button[/input]. Try them out yourself later, but for now try hitting the buddy " +
            "with a [impact]charged swing[/impact].",
        TutorialStepIds.UnequipTool => DropToolLine(dropToolBinding),
        TutorialStepIds.OpenPaintBuddy =>
            "Your Buddy could use a little colour. Open [action]Paint ▸ Buddy[/action] in the menu above to give " +
            "it a new look.",
        TutorialStepIds.SelectPaintBrush =>
            "There are many tools to choose from. Let's start with my favourite which is the " +
            "[action]Brush tool[/action] to start painting.",
        TutorialStepIds.PaintBuddy =>
            "[playful]Paint away![/playful] When you are happy, click on the [action]'Save'[/action] button below.",
        TutorialStepIds.UsePaintedBuddy =>
            "Click on [action]'Use Character'[/action] to start playing with your new Buddy!",
        TutorialStepIds.AdmirePaintedBuddy =>
            "[playful]Beautiful! Buddy has never looked better.[/playful]",
        TutorialStepIds.OpenPaintBackground =>
            "Your Buddy deserves a better room that matches its new style. Open [action]Paint ▸ Background[/action] " +
            "to start painting the room.",
        TutorialStepIds.SelectBackgroundSpray =>
            "Let's go with something [playful]new but nostalgic[/playful], the [action]Spray tool[/action]!",
        TutorialStepIds.SelectBackgroundColor =>
            "Choose a nice matching colour from the [action]Palette[/action].",
        TutorialStepIds.PaintBackground =>
            "[input]Click and drag[/input] anywhere on the room behind your Buddy to spray it.",
        TutorialStepIds.FloatPaintBackgroundPanel =>
            "We need to admire your drawing more. Drag any panel by its [input]title bar[/input] and move it " +
            "outside of the game window. You can also click the [input]red pin button[/input].",
        TutorialStepIds.SaveAndExitPaintBackground =>
            "[playful]Looks good![/playful] Click [action]Save and Exit[/action] to keep it.",
        TutorialStepIds.OpenBuddyStudio =>
            "Now let's bring your buddy more up to style. Open [action]Buddy Studio[/action] so we can customise " +
            "your Buddy with lots of apparel.",
        TutorialStepIds.SelectNoseCategory =>
            "Let's see, your buddy could use a new nose. Click on the [action]Nose category[/action] to see what " +
            "we've got.",
        TutorialStepIds.SelectNoseButtonStyle =>
            "This [playful]'Button nose'[/playful] could be fun! Click on it once to preview it without buying it.",
        TutorialStepIds.BuyStudioItem =>
            "[playful]It looks great![/playful] Let's click on the [action]'Buy'[/action] button. Alternatively, you can " +
            "[input]double-click[/input] on an item to buy it.",
        TutorialStepIds.EquipStudioItem =>
            "Let's [action]equip it[/action] for now. You can swap it later, or choose the default style to remove it.",
        TutorialStepIds.SaveBuddyStudio =>
            "Click on the [action]'Save'[/action] button to keep this [playful]beautiful nose[/playful].",
        TutorialStepIds.AdmireStudioBuddy =>
            "[playful]Now that is what I call a nose. Now your buddy is looking mighty fine![/playful]",
        TutorialStepIds.EnterWorkMode =>
            "Last but not least: [action]Work Mode[/action]. Enter work mode for when you need to concentrate and " +
            "want to let your buddy sit beside you.",
        TutorialStepIds.DragWorkCompanion =>
            "Click and hold on the Buddy with the [input]left mouse button[/input] to [action]drag[/action] your companion " +
            "wherever you want it.",
        TutorialStepIds.ResizeWorkCompanion =>
            "Click and hold the [input]resize button ↘[/input] with the [input]left mouse button[/input], then drag to make " +
            "your companion [action]bigger or smaller[/action].",
        TutorialStepIds.ToggleWorkCounter =>
            "Your Buddy will [money]earn money[/money] while you work. [input]Click on the screen[/input] to switch between " +
            "this session's total count and your lifetime total count.",
        TutorialStepIds.ExitWorkMode =>
            "Ready to head back to play mode? [input]Double-click[/input] on your Buddy or click on the [input]'X'[/input] " +
            "button to [action]return[/action].",
        TutorialStepIds.Farewell =>
            "Well that was it, I hope you'll become the [playful]best of buds![/playful] If you ever need help, " +
            "click on the [input]'?'[/input] in the title bar and hover over anything on screen for context, " +
            "or restart the tutorial from the settings screen. [playful]Have fun with your Buddy![/playful]",
        _ => string.Empty,
    };

    private static string DropToolLine(string binding)
    {
        string shownBinding = string.IsNullOrWhiteSpace(binding) ? LocalSettingsInputBindings.DefaultDropTool : binding.Trim();
        return "To unequip a tool you can switch in the [action]Inventory[/action] or press " +
               Tag(ExpressiveSemanticRole.Input, shownBinding) + " to drop it. You can re-equip dropped tools by " +
               "[input]double-clicking[/input] them. Try to drop it now.";
    }

    private static string Tag(ExpressiveSemanticRole role, string text)
    {
        // '[' is the only syntax introducer in the tiny semantic language. An unusual binding
        // containing it stays readable and simply skips emphasis rather than corrupting markup.
        if (text.Contains('['))
            return text;
        string tag = ExpressiveSemanticTags.Name(role);
        return $"[{tag}]{text}[/{tag}]";
    }
}

using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Onboarding;

/// <summary>Controls the walkthrough can hold shut while a step is asking for something else.</summary>
public enum TutorialWorkControl
{
    Drag,
    Resize,
    Counter,
    Motion,
    Exit,
}

/// <summary>
/// What the walkthrough currently forbids, for the few places the guidance controller's own
/// click lock cannot reach: controls inside the Work companion's separate window, and gestures
/// like double-click that ride on top of a control the step legitimately wants clicked.
///
/// <para>Nothing here is durable state. <see cref="FirstSessionGuidanceController"/> republishes
/// the displayed step every frame and publishes <c>null</c> when it leaves the tree, so a
/// finished, skipped or torn-down walkthrough reopens every gate on the same frame rather than
/// depending on some teardown path remembering to unlock. Ask, never latch.</para>
/// </summary>
public static class TutorialInputGate
{
    /// <summary>The step on screen, or null when no prompt is showing.</summary>
    public static string? Step { get; private set; }

    public static bool WalkthroughActive => Step is not null;

    /// <summary>
    /// Editing a palette block is a double-click on the very swatch the colour step wants
    /// single-clicked, so the step cannot lock it out by rectangle. It comes back the moment
    /// the walkthrough ends.
    /// </summary>
    public static bool AllowsPaletteEditing => !WalkthroughActive;

    /// <summary>
    /// While a Work step is asking for one control, that control is the only one that answers.
    /// Otherwise the companion behaves normally — including during the non-Work steps, when the
    /// companion is not on screen at all.
    /// </summary>
    public static bool Allows(TutorialWorkControl control) => Step switch
    {
        TutorialStepIds.DragWorkCompanion => control == TutorialWorkControl.Drag,
        TutorialStepIds.ResizeWorkCompanion => control == TutorialWorkControl.Resize,
        TutorialStepIds.ToggleWorkCounter => control == TutorialWorkControl.Counter,
        // Exiting is the lesson here, and the pause toggle would only strand the player.
        TutorialStepIds.ExitWorkMode => control == TutorialWorkControl.Exit,
        _ => true,
    };

    internal static void Publish(string? stepId) => Step = stepId;
}

namespace DesktopBuddy.Onboarding;

/// <summary>
/// The Work Mode seam for distributions that do not ship Work Mode.
///
/// <para>Compiled in place of <c>FirstSessionGuidanceController.Work.cs</c> when the itch scope
/// removes <c>src/Work</c>, so the controller compiles with no <c>WorkCompanionCoordinator</c> or
/// <c>WorkCompanionView</c> in the assembly. Work is never active there, which is the truth in a
/// build that has no Work Mode rather than a stub standing in for something hidden.</para>
///
/// <para>Unreachable in practice: the Work Mode steps are already absent from the itch step
/// sequence, so the walkthrough never asks.</para>
/// </summary>
public sealed partial class FirstSessionGuidanceController
{
    private void DiscoverWork()
    {
    }

    private void ResetWorkCounterBaseline()
    {
    }

    private bool IsWorkActive() => false;

    private Godot.Window? WorkCompanionWindow() => null;

    private bool IsWorkCompanionDragging() => false;

    private bool HasSwitchedWorkCounter() => false;
}

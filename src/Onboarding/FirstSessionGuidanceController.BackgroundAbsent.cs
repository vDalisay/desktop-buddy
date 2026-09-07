using Godot;

namespace DesktopBuddy.Onboarding;

/// <summary>
/// The Paint Background seam for distributions that do not ship the room workspace.
///
/// <para>Compiled in place of <c>FirstSessionGuidanceController.Background.cs</c> when the itch
/// scope removes <c>src/Environment</c>, so the controller compiles with no
/// <c>EnvironmentBackgroundEditor</c> or <c>EnvironmentBackgroundPresenter</c> in the assembly.
/// Every question answers "there is no room workspace", which is the truth in that build.</para>
///
/// <para>Unreachable in practice: the Paint Background steps are already absent from the itch step
/// sequence, so the walkthrough never asks.</para>
/// </summary>
public sealed partial class FirstSessionGuidanceController
{
    private void DiscoverBackground()
    {
    }

    private void BindBackgroundSignals()
    {
    }

    private void UnbindBackgroundSignals()
    {
    }

    private bool IsBackgroundOpen() => false;

    private bool IsBackgroundEditorOpen() => false;

    private bool IsBackgroundEditorClosed() => false;

    private bool IsBackgroundSpraySelected() => false;

    private bool IsBackgroundCanvasDirty() => false;

    private bool IsBackgroundCanvasClean() => false;

    private bool HasChosenBackgroundColor() => false;

    private Node? BackgroundScope => null;

    private Control? BackgroundPanel() => null;
}

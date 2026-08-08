using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>
/// Reserved composition root for the Buddy Studio branch. It is intentionally inert on the
/// shared baseline: the branch may compose/register Buddy Studio here without touching
/// project.godot or the shared command-bar bootstrap.
/// </summary>
public partial class BuddyStudioBootstrap : Node
{
    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;
}

using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Platform;
using Godot;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Platform;

/// <summary>
/// Windows-safe full-screen interaction policy.
///
/// The separate native recovery toolbar proved unreliable above a transparent always-on-top
/// overlay. Full-screen therefore uses the already proven same-window dock and remains in Play
/// mode, so both the buddy and dock stay visible and interactive. Compact mode retains the
/// normal Work/Play toggle and desktop click-through behavior.
/// </summary>
public partial class DesktopShellController
{
    private const string FullscreenPolicyCategory = "FullscreenPlayPolicy";

    private bool _fullscreenPolicyAnnounced;

    public override void _Process(double delta)
    {
        bool fullscreen = Window.LayoutMode == WindowLayoutMode.FullscreenOverlay;

        // Whole-window passthrough makes every same-window control unreachable. Full-screen
        // is therefore Play-only; Work mode remains available in the compact window.
        if (fullscreen && _mode.Current != DomainInputMode.Play && !EditorBoundaryIsolationActive)
        {
            Apply(ShellInputEvent.GlobalToggle);
            Log.Info(FullscreenPolicyCategory,
                "Corrected full-screen overlay to Play mode so the buddy and dock accept input.");
        }

        SceneTree tree = GetTree();
        if (tree?.Root is null)
            return;

        Control? editorPanel = tree.Root.FindChild(
            "CharacterEditorPanel",
            recursive: true,
            owned: false) as Control;
        Control? dock = tree.Root.FindChild(
            "FloatingDock",
            recursive: true,
            owned: false) as Control;
        Button? modeButton = tree.Root.FindChild(
            "DockInteractionModeButton",
            recursive: true,
            owned: false) as Button;
        Godot.Window? failedToolbar = tree.Root.FindChild(
            "DesktopToolbarWindow",
            recursive: true,
            owned: false) as Godot.Window;

        if (GodotObject.IsInstanceValid(failedToolbar) && failedToolbar!.Visible)
            failedToolbar.Hide();

        if (GodotObject.IsInstanceValid(modeButton))
        {
            modeButton!.Disabled = fullscreen;
            if (fullscreen)
            {
                modeButton.Text = "PLAY";
                modeButton.TooltipText =
                    "Full-screen stays in Play mode so its dock and buddy remain interactive. " +
                    "Return to the compact window to use Work mode.";
            }
        }

        if (!fullscreen)
        {
            _fullscreenPolicyAnnounced = false;
            return;
        }

        bool editorOpen = GodotObject.IsInstanceValid(editorPanel) && editorPanel!.Visible;
        if (GodotObject.IsInstanceValid(dock))
        {
            dock!.Visible = !editorOpen;
            dock.MouseFilter = Control.MouseFilterEnum.Stop;
        }

        if (!_fullscreenPolicyAnnounced && GodotObject.IsInstanceValid(dock))
        {
            _fullscreenPolicyAnnounced = true;
            Log.Info(FullscreenPolicyCategory,
                $"Full-screen in-window dock enabled: visible={dock!.Visible} " +
                $"visibleInTree={dock.IsVisibleInTree()} rect={dock.GetGlobalRect()}.");
        }
    }
}

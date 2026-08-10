using System;
using System.Threading.Tasks;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>
/// Debug-only normal-boot oracle for the real autoload/menu/workspace path. Unlike a scenario,
/// this runs after the production sandbox and command bar compose.
/// </summary>
internal static class BuddyStudioStartupProbe
{
    private const string Category = "BuddyStudioStartupProbe";

    public static async Task<bool> RunAsync(SceneTree tree)
    {
        PopupMenu? popup = null;
        int studioIndex = -1;
        for (int frame = 0; frame < 300 && studioIndex < 0; frame++)
        {
            studioIndex = FindStudioCommand(tree.Root, out popup);
            if (studioIndex < 0)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        if (studioIndex < 0 || popup is null)
            return Verdict(false, popup is null
                ? "No command-bar menu button was composed."
                : $"Menu items were [{PopupItems(popup)}].");
        if (popup.IsItemDisabled(studioIndex))
            return Verdict(false, "Buddy Studio was present but disabled.");

        popup.EmitSignal(PopupMenu.SignalName.IdPressed, popup.GetItemId(studioIndex));
        CharacterEditorHost? host = null;
        BuddyStudioWorkspace? workspace = null;
        for (int frame = 0; frame < 300; frame++)
        {
            host = tree.Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
            workspace = tree.Root.FindChild(nameof(BuddyStudioWorkspace), true, false) as BuddyStudioWorkspace;
            if (GodotObject.IsInstanceValid(host) && host!.IsEditorOpen &&
                GodotObject.IsInstanceValid(workspace) && workspace!.IsVisibleInTree())
            {
                bool paintButtonAbsent = workspace.FindChild("PaintModeButton", true, false) is null;
                bool paintCanvasHidden = workspace.FindChild(
                    "CharacterPaintCanvas", true, false) is not Control paintCanvas || !paintCanvas.Visible;
                var windowFrame = tree.Root.FindChild(
                    nameof(UI.Win98.Win98WindowFrame), true, false) as UI.Win98.Win98WindowFrame;
                bool singleStatusBar = workspace.FindChild("CharacterEditorStatus", true, false) is null &&
                    workspace.FindChild("BuddyStudioStatus", true, false) is null &&
                    GodotObject.IsInstanceValid(windowFrame) &&
                    !string.IsNullOrWhiteSpace(windowFrame!.StatusText);
                bool workingCharacterLoaded = host.Session.WorkingDocument is not null && host.Session.CanSave;
                bool viewReady = workspace.FindChild("BuddyStudioZoomOut", true, false) is Button &&
                    workspace.FindChild("BuddyStudioZoomIn", true, false) is Button &&
                    workspace.FindChild("BuddyStudioResetView", true, false) is Button &&
                    workspace.PreviewFocus.IsEqualApprox(new Vector2(0, 50)) &&
                    workspace.PreviewCameraSize < 150;
                if (!paintButtonAbsent || !paintCanvasHidden || !singleStatusBar ||
                    !workingCharacterLoaded || !viewReady)
                    return Verdict(false,
                        $"Studio readiness failed: buttonAbsent={paintButtonAbsent} canvasHidden={paintCanvasHidden} " +
                        $"singleStatusBar={singleStatusBar} working={workingCharacterLoaded} view={viewReady}.");
                return Verdict(true,
                    $"items=[{PopupItems(popup)}] workspace={workspace.GetPath()} paintHidden=true " +
                    $"working={host.Session.WorkingDocument!.Id} focus={workspace.PreviewFocus} size={workspace.PreviewCameraSize}");
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        return Verdict(false,
            $"Buddy Studio command did not open: hostOpen={host?.IsEditorOpen} " +
            $"workspaceValid={GodotObject.IsInstanceValid(workspace)} visible={workspace?.IsVisibleInTree()}.");
    }

    /// <summary>
    /// Locates the Buddy Studio command by the item it registers rather than by the menu's
    /// label, which is presentation and has already been renamed once ("Customize" to "Paint").
    /// </summary>
    private static int FindStudioCommand(Node node, out PopupMenu? popup)
    {
        popup = null;
        if (node is MenuButton button)
        {
            PopupMenu candidate = button.GetPopup();
            candidate.EmitSignal(PopupMenu.SignalName.AboutToPopup);
            popup = candidate;
            for (int index = 0; index < candidate.ItemCount; index++)
            {
                if (string.Equals(candidate.GetItemText(index), "Buddy Studio", StringComparison.Ordinal))
                    return index;
            }
        }

        foreach (Node child in node.GetChildren())
        {
            int found = FindStudioCommand(child, out PopupMenu? childPopup);
            if (found >= 0)
            {
                popup = childPopup;
                return found;
            }
            popup ??= childPopup;
        }
        return -1;
    }

    private static string PopupItems(PopupMenu popup)
    {
        string[] items = new string[popup.ItemCount];
        for (int index = 0; index < items.Length; index++)
            items[index] = popup.GetItemText(index);
        return string.Join(", ", items);
    }

    private static bool Verdict(bool passed, string detail)
    {
        string text = $"passed={passed.ToString().ToLowerInvariant()} detail={detail}";
        if (passed)
            Log.Info(Category, text);
        else
            Log.Error(Category, text);
        GD.Print($"[BUDDY_STUDIO_STARTUP] {text}");
        return passed;
    }
}

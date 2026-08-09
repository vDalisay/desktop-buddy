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
        MenuButton? customize = null;
        for (int frame = 0; frame < 300 && !GodotObject.IsInstanceValid(customize); frame++)
        {
            customize = FindCustomizeButton(tree.Root);
            if (!GodotObject.IsInstanceValid(customize))
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        if (!GodotObject.IsInstanceValid(customize))
            return Verdict(false, "Customize menu button was not composed.");

        PopupMenu popup = customize!.GetPopup();
        popup.EmitSignal(PopupMenu.SignalName.AboutToPopup);
        int studioIndex = -1;
        for (int index = 0; index < popup.ItemCount; index++)
        {
            if (string.Equals(popup.GetItemText(index), "Buddy Studio", StringComparison.Ordinal))
            {
                studioIndex = index;
                break;
            }
        }
        if (studioIndex < 0)
            return Verdict(false, $"Customize items were [{PopupItems(popup)}].");
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
                bool sharedStatus = workspace.FindChild("CharacterEditorStatus", true, false) is Label &&
                    workspace.FindChild("BuddyStudioStatus", true, false) is null;
                bool workingCharacterLoaded = host.Session.WorkingDocument is not null && host.Session.CanSave;
                bool viewReady = workspace.FindChild("BuddyStudioZoomOut", true, false) is Button &&
                    workspace.FindChild("BuddyStudioZoomIn", true, false) is Button &&
                    workspace.FindChild("BuddyStudioResetView", true, false) is Button &&
                    workspace.PreviewFocus.IsEqualApprox(new Vector2(0, 50)) &&
                    workspace.PreviewCameraSize < 150;
                if (!paintButtonAbsent || !paintCanvasHidden || !sharedStatus ||
                    !workingCharacterLoaded || !viewReady)
                    return Verdict(false,
                        $"Studio readiness failed: buttonAbsent={paintButtonAbsent} canvasHidden={paintCanvasHidden} " +
                        $"sharedStatus={sharedStatus} working={workingCharacterLoaded} view={viewReady}.");
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

    private static MenuButton? FindCustomizeButton(Node node)
    {
        if (node is MenuButton { Text: "Customize" } button)
            return button;
        foreach (Node child in node.GetChildren())
        {
            MenuButton? found = FindCustomizeButton(child);
            if (GodotObject.IsInstanceValid(found))
                return found;
        }
        return null;
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

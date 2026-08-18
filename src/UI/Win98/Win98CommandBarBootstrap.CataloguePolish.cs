using Godot;

namespace DesktopBuddy.UI.Win98;

public partial class Win98CommandBarBootstrap
{
    /// <summary>
    /// The Shop panel now owns both purchase and equip actions, so the former Tools command is
    /// deliberately retired rather than leaving two competing ways to choose the active tool.
    /// This bootstrap waits only until the shared command bar is composed, applies the stable
    /// chrome once, then disables its physics callback instead of rewriting the same UI at 120 Hz.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (!_composed || !GodotObject.IsInstanceValid(_shopButton) ||
            !GodotObject.IsInstanceValid(_toolsButton))
        {
            return;
        }

        _shopButton.Text = "Inventory";
        _shopButton.TooltipText = "Buy and equip tools.";
        _shopButton.CustomMinimumSize = new Vector2(84, 22);
        _toolsButton.Visible = false;

        if (GodotObject.IsInstanceValid(_editorHost?.ToolsButton))
            _editorHost.ToolsButton.Visible = false;

        if (ReferenceEquals(_activeSection, _tools))
            CloseFlyout();
        RefreshInventoryFlyoutTitle();

        // OpenSection still receives the legacy internal title "Shop". This handler was added
        // after the original press handler, so it normalizes the visible title after every open
        // without keeping an always-on polling loop alive.
        _shopButton.Pressed += RefreshInventoryFlyoutTitle;
        SetPhysicsProcess(false);
    }

    private void RefreshInventoryFlyoutTitle()
    {
        if (ReferenceEquals(_activeSection, _shop) && GodotObject.IsInstanceValid(_flyoutTitle))
            _flyoutTitle.Text = "Inventory";
    }
}
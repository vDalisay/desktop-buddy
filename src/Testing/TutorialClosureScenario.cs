using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Onboarding;
using DesktopBuddy.Persistence;
using DesktopBuddy.Shop;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Runtime seam for the concise v2 tutorial. The existing Paint/Studio/Work scenarios own the
/// deep workspace behavior; this scenario proves the onboarding controller is wired to real
/// gameplay state, uses Win98 chrome, exposes permanent Help, and does not skip from Inventory
/// straight to Work.
/// </summary>
public sealed class TutorialClosureScenario : IScenario
{
    public string Id => "tutorial_closure";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        SandboxRoot? sandbox = null;
        FirstSessionGuidanceController? guidance = null;
        ShopPanel? shop = null;

        try
        {
            // Headless roots default to 64x64; the tutorial window's placement is only meaningful
            // against a viewport of roughly the shipped Compact size.
            tree.Root.Size = new Vector2I(1280, 940);
            await Frames(tree, 1);

            PackedScene? packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
            sandbox = packed?.Instantiate() as SandboxRoot;
            if (sandbox is null)
            {
                checks.Add(new StartupCheck("tutorial_sandbox_loads", false, "res://scenes/sandbox.tscn"));
                return new ScenarioResult(false, checks, messages);
            }

            double cashPerPain = sandbox.Pipeline.RequirePainProfile().CashPerPain;
            var progress = new BuddyProgressState(cashPerPain);
            var economy = new EconomyService(progress, CatalogueLoader.Catalogue);
            var store = new InMemoryProgressStore();
            var saves = new SaveCoordinator(progress, store);
            var context = new RunContext(
                progress,
                economy,
                store,
                saves,
                new LocalSettingsSave(),
                SaveLoadStatus.NewSave);

            sandbox.Configure(context);
            tree.Root.AddChild(sandbox);
            await Frames(tree, 3);

            // This scenario intentionally loads the isolated sandbox scene rather than the full
            // application composition, so CharacterEditorHost/Win98CommandBarBootstrap are absent.
            // Mount the production ShopPanel directly: command-bar navigation already has its own
            // Win98 scenario, while this test needs the real visibility + purchase/equip authority
            // that FirstSessionGuidanceController observes.
            shop = new ShopPanel
            {
                Visible = false,
            };
            tree.Root.AddChild(shop);
            shop.Configure(progress, economy, CatalogueLoader.Catalogue, sandbox.Pipeline);
            await Frames(tree, 1);

            guidance = new FirstSessionGuidanceController
            {
                Name = nameof(FirstSessionGuidanceController),
            };
            guidance.Configure(sandbox, context);
            tree.Root.AddChild(guidance);
            await Frames(tree, 2);

            PanelContainer? tutorialPanel = guidance.FindChild(
                "FirstSessionGuidancePanel", true, false) as PanelContainer;
            PanelContainer? titleBar = guidance.FindChild(
                "FirstSessionGuidancePanelTitleBar", true, false) as PanelContainer;
            StyleBoxFlat? titleStyle = titleBar?.GetThemeStylebox("panel") as StyleBoxFlat;
            Button? help = guidance.FindChild("ContextHelpButton", true, false) as Button;
            Control? spotlight = guidance.FindChild("ContextHelpSpotlight", true, false) as Control;

            bool win98Chrome = GodotObject.IsInstanceValid(tutorialPanel) &&
                GodotObject.IsInstanceValid(titleBar) &&
                titleStyle is not null &&
                titleStyle.BgColor == Win98ThemeFactory.ActiveTitle &&
                tutorialPanel!.Visible;
            checks.Add(new StartupCheck(
                "tutorial_uses_win98_active_title_chrome",
                win98Chrome,
                $"panel={GodotObject.IsInstanceValid(tutorialPanel)} title={GodotObject.IsInstanceValid(titleBar)} " +
                $"active={(titleStyle?.BgColor == Win98ThemeFactory.ActiveTitle)}"));

            // The guide art is a square inside the one tutorial window, not a second window.
            Node? guideArt = guidance.FindChild("DemoTutorialBuddy", true, false);
            checks.Add(new StartupCheck(
                "tutorial_guide_art_lives_inside_the_tutorial_window",
                GodotObject.IsInstanceValid(tutorialPanel) && GodotObject.IsInstanceValid(guideArt) &&
                tutorialPanel!.IsAncestorOf(guideArt),
                $"panel={GodotObject.IsInstanceValid(tutorialPanel)} art={GodotObject.IsInstanceValid(guideArt)} " +
                $"inside={GodotObject.IsInstanceValid(guideArt) && tutorialPanel?.IsAncestorOf(guideArt) == true}"));

            // Home is the middle of the right edge, not the top-left default a Control lands on.
            Vector2 viewport = tree.Root.GetVisibleRect().Size;
            Rect2 panelRect = tutorialPanel?.GetGlobalRect() ?? new Rect2();
            float panelCentreY = panelRect.Position.Y + (panelRect.Size.Y * 0.5f);
            bool rightMiddle = panelRect.Size.X > 1 &&
                panelRect.Position.X > viewport.X * 0.5f &&
                Math.Abs(panelCentreY - (viewport.Y * 0.5f)) <= viewport.Y * 0.15f;
            checks.Add(new StartupCheck(
                "tutorial_window_opens_middle_right",
                rightMiddle,
                $"panel={panelRect} viewport={viewport}"));

            bool startsAtGrab = guidance.Progress.NextIncompleteStepId == TutorialStepIds.GrabBuddy;
            checks.Add(new StartupCheck(
                "tutorial_v2_starts_at_real_grab",
                startsAtGrab,
                $"next={guidance.Progress.NextIncompleteStepId}"));

            bool helpPresent = GodotObject.IsInstanceValid(help) && help!.Visible &&
                string.Equals(help.Text, "Help", StringComparison.Ordinal);
            if (helpPresent)
            {
                help!.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(tree, 1);
            }
            bool helpOpened = helpPresent && guidance.ContextHelpActive &&
                GodotObject.IsInstanceValid(spotlight) && spotlight!.Visible;
            checks.Add(new StartupCheck(
                "permanent_help_toggle_activates_spotlight",
                helpOpened,
                $"button={helpPresent} active={guidance.ContextHelpActive} spotlight={spotlight?.Visible}"));
            // Help mode must always offer a plain way out, not just the small title-bar icon.
            var exitHelp = guidance.FindChild("ExitHelpModeButton", true, false) as Button;
            checks.Add(new StartupCheck(
                "help_mode_offers_an_exit_button",
                guidance.ContextHelpActive && GodotObject.IsInstanceValid(exitHelp) && exitHelp!.Visible,
                $"active={guidance.ContextHelpActive} button={GodotObject.IsInstanceValid(exitHelp)} " +
                $"visible={exitHelp?.Visible}"));

            if (GodotObject.IsInstanceValid(exitHelp) && guidance.ContextHelpActive)
            {
                exitHelp!.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(tree, 1);
            }
            checks.Add(new StartupCheck(
                "exit_button_leaves_help_mode",
                !guidance.ContextHelpActive && exitHelp?.Visible == false,
                $"active={guidance.ContextHelpActive} visible={exitHelp?.Visible}"));

            if (GodotObject.IsInstanceValid(help) && guidance.ContextHelpActive)
            {
                help!.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(tree, 1);
            }

            // Holding Buddy must not advance the walkthrough: letting go is the taught action,
            // otherwise the next prompt spotlights itself while the player is still dragging.
            Vector2 torsoPoint = sandbox.Buddy.Rig.Torso.GlobalPosition;
            bool grabbed = sandbox.Grab.TryGrab(sandbox.Buddy.Rig.Torso, torsoPoint);
            await Frames(tree, 2);
            string? whileHeld = guidance.Progress.NextIncompleteStepId;
            sandbox.Grab.Release();
            await Frames(tree, 2);
            string? afterGrab = guidance.Progress.NextIncompleteStepId;
            checks.Add(new StartupCheck(
                "buddy_grab_advances_only_after_release",
                grabbed && whileHeld == TutorialStepIds.GrabBuddy && afterGrab == TutorialStepIds.OpenInventory,
                $"grabbed={grabbed} held={whileHeld} released={afterGrab}"));

            // The walkthrough's next ask is a 1-credit purchase, so that first handful of Buddy
            // has to cover it — real manhandling pays fractions of a credit.
            checks.Add(new StartupCheck(
                "first_grab_pays_enough_for_the_baseball_bat",
                progress.BalanceMilliCredits >= 1_000,
                $"balance={progress.BalanceMilliCredits}"));

            // Every readout quotes the same whole-credit number; a panel showing $0.2 beside a
            // corner showing $0 is the same money described two ways.
            checks.Add(new StartupCheck(
                "credits_render_as_whole_credits_everywhere",
                DesktopBuddy.Ui.ContentDisplayName.Credits(1_200) == "$1" &&
                DesktopBuddy.Ui.ContentDisplayName.Credits(200) == "$0",
                $"1200={DesktopBuddy.Ui.ContentDisplayName.Credits(1_200)} " +
                $"200={DesktopBuddy.Ui.ContentDisplayName.Credits(200)}"));

            // Earning is taught inside the purchase prompt rather than as a step of its own, so
            // money arriving must not advance anything by itself.
            progress.Deposit(1_000);
            await Frames(tree, 2);
            checks.Add(new StartupCheck(
                "earning_credits_is_not_a_step_of_its_own",
                guidance.Progress.NextIncompleteStepId == TutorialStepIds.OpenInventory,
                $"next={guidance.Progress.NextIncompleteStepId}"));

            // Give the deterministic journey enough money for the authored bat price without
            // bypassing the Shop purchase path that the tutorial is supposed to observe.
            progress.Deposit(1_000_000);
            shop.Refresh();
            shop.Visible = true;
            await Frames(tree, 2);

            string? afterInventory = guidance.Progress.NextIncompleteStepId;
            checks.Add(new StartupCheck(
                "showing_real_shop_panel_advances_to_baseball_bat_purchase",
                shop.IsVisibleInTree() && afterInventory == TutorialStepIds.PurchaseBaseballBat,
                $"visible={shop.IsVisibleInTree()} next={afterInventory}"));

            Button? batAction = shop.BuyButtonFor(ContentIds.ToolBaseballBat);
            if (GodotObject.IsInstanceValid(batAction))
            {
                batAction!.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(tree, 2);
            }
            string? afterBuy = guidance.Progress.NextIncompleteStepId;

            // A single Buy press must both own and equip the bat: the tutorial no longer teaches
            // a separate equip step, so a purchase that left the tool unequipped would strand it.
            checks.Add(new StartupCheck(
                "buying_real_baseball_bat_auto_equips_and_advances_to_charged_hit",
                progress.IsToolUnlocked(ContentIds.ToolBaseballBat) &&
                sandbox.Pipeline.SelectedTool == DesktopBuddy.Domain.Tools.ToolId.BaseballBat &&
                afterBuy == TutorialStepIds.ChargedBatHit,
                $"action={batAction?.Text} owned={progress.IsToolUnlocked(ContentIds.ToolBaseballBat)} " +
                $"tool={sandbox.Pipeline.SelectedTool} next={afterBuy}"));

            // Owning the bat is not the lesson; releasing a charged swing is. Selecting the tool
            // again must not be mistaken for having demonstrated the taught control.
            sandbox.Pipeline.Progress.SelectTool(DesktopBuddy.Domain.Tools.ToolId.BaseballBat);
            await Frames(tree, 2);
            checks.Add(new StartupCheck(
                "bat_charge_step_is_not_satisfied_by_merely_holding_the_bat",
                guidance.Progress.NextIncompleteStepId == TutorialStepIds.ChargedBatHit,
                $"next={guidance.Progress.NextIncompleteStepId}"));

            // The lesson teaches the RMB charge/release control, not full power or contact. A
            // short real charge must advance even when the swing misses Buddy.
            sandbox.CursorTools.MoveCursor(sandbox.Boundaries.InnerBounds.GetCenter());
            sandbox.Pipeline.SelectTool(DesktopBuddy.Domain.Tools.ToolId.BaseballBat);
            await PhysicsFrames(tree, 3);
            sandbox.CursorTools.SetGrip(true);
            await PhysicsFrames(tree, 3);
            sandbox.CursorTools.SetChargeHeld(true);
            await PhysicsFrames(tree, 3);
            int partialChargeTicks = sandbox.CursorTools.SwingChargeTicks;
            sandbox.CursorTools.SetChargeHeld(false);
            await PhysicsFrames(tree, 2);
            await Frames(tree, 2);
            checks.Add(new StartupCheck(
                "any_bat_charge_release_advances_tutorial",
                sandbox.CursorTools.IsSwingCapable && partialChargeTicks > 0 && partialChargeTicks < 300 &&
                guidance.Progress.NextIncompleteStepId == TutorialStepIds.UnequipTool,
                $"initialized={sandbox.CursorTools.IsInitialized} cursor={sandbox.CursorTools.HasCursor} " +
                $"active={sandbox.CursorTools.IsActive} content={sandbox.CursorTools.ActiveContentId ?? "none"} " +
                $"selected={sandbox.Pipeline.SelectedTool} swingCapable={sandbox.CursorTools.IsSwingCapable} " +
                $"state={sandbox.CursorTools.SwingState} chargeTicks={partialChargeTicks} " +
                $"epoch={sandbox.CursorTools.SwingEpoch} next={guidance.Progress.NextIncompleteStepId}"));
            sandbox.CursorTools.SetGrip(false);
            sandbox.CursorTools.SetChargeHeld(false);

            int workStart = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.EnterWorkMode);
            int paintBuddy = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.OpenPaintBuddy);
            int createBuddy = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.CreateBuddy);
            int selectBrush = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.SelectPaintBrush);
            int paintBackground = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.OpenPaintBackground);
            // Paint needs something to paint on: create and name have to land after the editor
            // opens and before the first brush lesson, or the player meets a disabled Save.
            checks.Add(new StartupCheck(
                "character_is_created_and_named_before_painting",
                paintBuddy >= 0 && createBuddy == paintBuddy + 1 && selectBrush == createBuddy + 1,
                $"openPaintBuddy={paintBuddy} create={createBuddy} brush={selectBrush}"));
            int buddyStudio = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.OpenBuddyStudio);
            checks.Add(new StartupCheck(
                "work_remains_terminal_after_customization_screens",
                TutorialStepIds.Ordered[^6] == TutorialStepIds.EnterWorkMode &&
                TutorialStepIds.Ordered[^2] == TutorialStepIds.ExitWorkMode &&
                TutorialStepIds.Ordered[^1] == TutorialStepIds.Farewell &&
                paintBuddy >= 0 && paintBuddy < workStart &&
                paintBackground >= 0 && paintBackground < workStart &&
                buddyStudio >= 0 && buddyStudio < workStart,
                $"paintBuddy={paintBuddy} background={paintBackground} studio={buddyStudio} work={workStart}"));

            // Replaying preserves ownership, but the Inventory lesson must still appear and ring
            // the bat action. Restarting returns to bare hands, so an owned bat offers Equip.
            guidance.RestartTutorial();
            shop.Refresh();
            await Frames(tree, 2);
            Vector2 replayTorsoPoint = sandbox.Buddy.Rig.Torso.GlobalPosition;
            bool replayGrabbed = sandbox.Grab.TryGrab(sandbox.Buddy.Rig.Torso, replayTorsoPoint);
            await Frames(tree, 2);
            sandbox.Grab.Release();
            await Frames(tree, 4);
            Button? replayBatAction = shop.BuyButtonFor(ContentIds.ToolBaseballBat);
            Control? tutorialSpotlight = guidance.FindChild("TutorialSpotlight", true, false) as Control;
            checks.Add(new StartupCheck(
                "owned_bat_replay_keeps_purchase_step_and_highlight",
                replayGrabbed && guidance.DisplayedStepId == TutorialStepIds.PurchaseBaseballBat &&
                tutorialSpotlight?.Visible == true && replayBatAction?.Text == "Equip" &&
                replayBatAction.Disabled == false,
                $"grabbed={replayGrabbed} displayed={guidance.DisplayedStepId} " +
                $"spotlight={tutorialSpotlight?.Visible} action={replayBatAction?.Text} " +
                $"disabled={replayBatAction?.Disabled}"));
            if (GodotObject.IsInstanceValid(replayBatAction) && replayBatAction!.Disabled == false)
            {
                replayBatAction.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(tree, 2);
            }
            checks.Add(new StartupCheck(
                "owned_bat_equip_advances_replayed_purchase_step",
                sandbox.Pipeline.SelectedTool == DesktopBuddy.Domain.Tools.ToolId.BaseballBat &&
                guidance.Progress.NextIncompleteStepId == TutorialStepIds.ChargedBatHit,
                $"tool={sandbox.Pipeline.SelectedTool} next={guidance.Progress.NextIncompleteStepId}"));

            // Skipping ends the walkthrough, and an ended walkthrough must hand every control
            // back: no prompt on screen means no input lock, anywhere.
            guidance.SkipTutorial();
            await Frames(tree, 2);
            PanelContainer? prompt = guidance.FindChild(
                "FirstSessionGuidancePanel", true, false) as PanelContainer;
            checks.Add(new StartupCheck(
                "finishing_the_tutorial_releases_the_input_lock",
                guidance.DisplayedStepId is null && prompt?.Visible == false,
                $"displayed={guidance.DisplayedStepId ?? "none"} prompt={prompt?.Visible}"));
        }
        finally
        {
            if (GodotObject.IsInstanceValid(guidance))
                guidance!.QueueFree();
            if (GodotObject.IsInstanceValid(shop))
                shop!.QueueFree();
            if (GodotObject.IsInstanceValid(sandbox))
                sandbox!.QueueFree();
            await Frames(tree, 2);
        }

        bool passed = checks.All(static check => check.Passed);
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task Frames(SceneTree tree, int count)
    {
        for (int index = 0; index < count; index++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    private static async Task PhysicsFrames(SceneTree tree, int count)
    {
        for (int index = 0; index < count; index++)
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }
}

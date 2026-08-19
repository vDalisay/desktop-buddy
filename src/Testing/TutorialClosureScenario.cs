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
            if (GodotObject.IsInstanceValid(help) && guidance.ContextHelpActive)
            {
                help!.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(tree, 1);
            }

            Vector2 torsoPoint = sandbox.Buddy.Rig.Torso.GlobalPosition;
            bool grabbed = sandbox.Grab.TryGrab(sandbox.Buddy.Rig.Torso, torsoPoint);
            await Frames(tree, 2);
            string? afterGrab = guidance.Progress.NextIncompleteStepId;
            sandbox.Grab.Release();
            checks.Add(new StartupCheck(
                "real_buddy_grab_advances_tutorial",
                grabbed && afterGrab == TutorialStepIds.EarnCredits,
                $"grabbed={grabbed} next={afterGrab}"));

            progress.Deposit(1_000);
            await Frames(tree, 2);
            string? afterEarn = guidance.Progress.NextIncompleteStepId;
            checks.Add(new StartupCheck(
                "real_balance_change_advances_to_inventory",
                afterEarn == TutorialStepIds.OpenInventory,
                $"next={afterEarn}"));

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
            checks.Add(new StartupCheck(
                "buying_real_baseball_bat_advances_to_equip",
                progress.IsToolUnlocked(ContentIds.ToolBaseballBat) && afterBuy == TutorialStepIds.EquipBaseballBat,
                $"action={batAction?.Text} owned={progress.IsToolUnlocked(ContentIds.ToolBaseballBat)} next={afterBuy}"));

            if (GodotObject.IsInstanceValid(batAction))
            {
                batAction!.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(tree, 2);
            }
            string? afterEquip = guidance.Progress.NextIncompleteStepId;
            checks.Add(new StartupCheck(
                "equipping_real_baseball_bat_advances_to_paint_not_work",
                sandbox.Pipeline.SelectedTool == DesktopBuddy.Domain.Tools.ToolId.BaseballBat &&
                afterEquip == TutorialStepIds.OpenPaintBuddy,
                $"tool={sandbox.Pipeline.SelectedTool} next={afterEquip}"));

            int workStart = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.EnterWorkMode);
            int paintBuddy = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.OpenPaintBuddy);
            int paintBackground = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.OpenPaintBackground);
            int buddyStudio = TutorialStepIds.Ordered.ToList().IndexOf(TutorialStepIds.OpenBuddyStudio);
            checks.Add(new StartupCheck(
                "work_remains_terminal_after_customization_screens",
                TutorialStepIds.Ordered[^4] == TutorialStepIds.EnterWorkMode &&
                TutorialStepIds.Ordered[^1] == TutorialStepIds.ExitWorkMode &&
                paintBuddy >= 0 && paintBuddy < workStart &&
                paintBackground >= 0 && paintBackground < workStart &&
                buddyStudio >= 0 && buddyStudio < workStart,
                $"paintBuddy={paintBuddy} background={paintBackground} studio={buddyStudio} work={workStart}"));
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
}

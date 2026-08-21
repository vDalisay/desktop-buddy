using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Economy;
using DesktopBuddy.Shop;
using DesktopBuddy.UI;
using DesktopBuddy.Work;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The reward popup demo and its oracle: a real purchase through <see cref="ShopPanel"/> plus a
/// Work milestone and a lifetime achievement, all queued on one frame so the sequential queue is
/// exercised, over the live sandbox composition. Saves a frame sequence per reward so the popup
/// can be judged without running the game.
///
/// <para>Run windowed (no <c>--headless</c>) for the screenshots; the semantic checks pass
/// either way.</para>
/// </summary>
public sealed class RewardPopupScenario : IScenario
{
    public string Id => "reward_popup_demo";

    /// <summary>Seconds after each popup starts at which a frame is captured.</summary>
    private static readonly double[] CaptureOffsets = [0.09, 0.75, 1.50];
    private const double PopupSeconds = 0.18 + 2.40 + 0.14;

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        if (tree.Root.GetNodeOrNull<RewardPopup>(nameof(RewardPopup)) is not { } popup)
        {
            return new ScenarioResult(false,
                [new StartupCheck("reward_popup_autoload_present", false, "RewardPopup autoload missing")],
                messages);
        }

        var loaded = await M4LifecycleScenarioSupport.Load(tree, new ManualMonotonicTimeSource());
        if (loaded is null)
        {
            return new ScenarioResult(false,
                [new StartupCheck("sandbox_loadable", false, "sandbox")], messages);
        }

        SandboxRoot sandbox = loaded.Value.Sandbox;
        BuddyProgressState progress = sandbox.Progress;
        EconomyService economy = sandbox.Economy;
        ToolCatalogue catalogue = CatalogueLoader.Catalogue;

        // The panel is only the trigger here: the demo is about what the popup does, so the
        // unstyled scenario-owned list stays out of the captured frames.
        var shop = new ShopPanel { Visible = false };
        tree.Root.AddChild(shop);
        shop.Configure(progress, economy, catalogue, sandbox.Pipeline);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        string directory = Path.GetFullPath(ScenarioArtifacts.Directory ?? ".artifacts/reward_popup_demo");
        Directory.CreateDirectory(directory);

        try
        {
            CatalogueEntry bought = CataloguePolicy.SelectableEntries(catalogue)
                .Where(static entry => !entry.IsStarting)
                .OrderByDescending(static entry => entry.PriceMilliCredits)
                .First();
            economy.DepositPassive(bought.PriceMilliCredits);
            shop.Refresh();

            // One frame, three rewards: a purchase through the production path, then the two
            // Work payouts. Exactly the collision the queue exists for.
            shop.BuyButtonFor(bought.ContentId)!.EmitSignal(BaseButton.SignalName.Pressed);
            RewardPopup.Show(
                shop,
                RewardIconProvider.For(RewardIconProvider.Milestone),
                "10,000 keystrokes this session",
                50 * RewardLedger.MilliCreditsPerCredit);
            RewardPopup.Show(
                shop,
                RewardIconProvider.For(RewardIconProvider.Trophy),
                "1,000,000 actions all time",
                1_000 * RewardLedger.MilliCreditsPerCredit);

            string[] labels = ["purchase", "milestone", "achievement"];
            string[] titles =
            [
                ContentDisplayName.For(bought.ContentId),
                "10,000 keystrokes this session",
                "1,000,000 actions all time",
            ];
            var captured = new List<string>();
            var titlesSeen = new List<string>();

            bool queuedOneAtATime = popup.IsShowing && popup.ShownCount == 1;
            checks.Add(new StartupCheck("reward_popup_queues_rather_than_stacking", queuedOneAtATime,
                $"showing={popup.IsShowing} shown={popup.ShownCount}"));

            // Headless has no rendered frame to save; the semantic checks are the authority and
            // the frames are owner evidence from a windowed run.
            bool headless = DisplayServer.GetName() == "headless";
            double elapsed = 0.0;
            int nextCapture = 0;
            double[] schedule = BuildSchedule();
            while (nextCapture < schedule.Length)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                elapsed += tree.Root.GetProcessDeltaTime();
                while (nextCapture < schedule.Length && elapsed >= schedule[nextCapture])
                {
                    int reward = nextCapture / CaptureOffsets.Length;
                    int frame = nextCapture % CaptureOffsets.Length;
                    if (frame == 0)
                        titlesSeen.Add(popup.CurrentTitle);
                    string path = Path.Combine(directory, $"reward_{reward + 1}_{labels[reward]}_{frame + 1}.png");
                    if (!headless && tree.Root.GetTexture().GetImage().SavePng(path) == Error.Ok && File.Exists(path))
                        captured.Add(path);
                    nextCapture++;
                }
            }

            bool playedInOrder = titlesSeen.Count == titles.Length &&
                titlesSeen.SequenceEqual(titles, StringComparer.Ordinal);
            checks.Add(new StartupCheck("reward_popup_plays_every_queued_reward_in_order", playedInOrder,
                $"seen=[{string.Join('|', titlesSeen)}] expected=[{string.Join('|', titles)}]"));
            checks.Add(new StartupCheck("reward_popup_showed_all_three_sources", popup.ShownCount == 3,
                $"shown={popup.ShownCount}"));

            // The copy the Work payouts will actually carry, straight from the shipped catalogue.
            var described = new List<string>();
            foreach (WorkMilestoneDefinition definition in WorkMilestoneDefaults.Create().Definitions)
            {
                (string title, string icon) = WorkCompanionCoordinator.DescribeMilestone(definition);
                described.Add($"{definition.Id}={icon}:{title}");
            }

            bool copyReads =
                described.Contains("work.session.keyboard.10000=milestone:10,000 keystrokes this session") &&
                described.Contains("work.lifetime.actions.1000000=trophy:1,000,000 actions all time");
            checks.Add(new StartupCheck("work_milestone_popup_copy_names_the_threshold", copyReads,
                string.Join(" | ", described)));

            checks.Add(new StartupCheck("reward_popup_frames_captured",
                headless || captured.Count == schedule.Length,
                $"headless={headless} captured={captured.Count}/{schedule.Length} dir={directory}"));
            messages.Add($"frames_directory={directory}");
            foreach (string path in captured)
                messages.Add($"frame={path}");
        }
        finally
        {
            shop.QueueFree();
            await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);
        }

        return new ScenarioResult(checks.All(static check => check.Passed), checks, messages);
    }

    private static double[] BuildSchedule()
    {
        var schedule = new List<double>();
        for (int reward = 0; reward < 3; reward++)
        {
            foreach (double offset in CaptureOffsets)
                schedule.Add((reward * PopupSeconds) + offset);
        }

        return schedule.ToArray();
    }
}

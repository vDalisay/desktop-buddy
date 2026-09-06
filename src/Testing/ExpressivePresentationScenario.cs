using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Onboarding;
using DesktopBuddy.UI;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Regression oracle for the expressive-presentation seams that are easy to accidentally undo:
/// live Drop Tool copy, first-click reveal completion, and the shared physics-free preview's
/// visible/hidden/capture render lifecycle.
/// </summary>
public sealed class ExpressivePresentationScenario : IScenario
{
    public string Id => "expressive_presentation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        // Dynamic input copy is resolved before semantic markup reaches the renderer. A rebound
        // chord must survive both the authoring layer and its plain-text projection.
        string rebound = "Ctrl+K";
        string dropSemantic = TutorialExpressiveCopy.Format(
            TutorialStepIds.UnequipTool,
            plainText: string.Empty,
            dropToolBinding: rebound);
        ExpressiveTextDocument dropDocument = ExpressiveSemanticMarkup.Parse(dropSemantic);
        checks.Add(new StartupCheck(
            "expressive_drop_prompt_uses_live_binding",
            dropDocument.PlainText.Contains($"press {rebound} to drop it", StringComparison.Ordinal) &&
            !dropDocument.PlainText.Contains("press D to drop it", StringComparison.Ordinal),
            dropDocument.PlainText));

        // The reusable presenter owns first-click behavior. Completing a reveal changes only the
        // presenter; there is deliberately no tutorial-state callback on this path.
        var presenter = new ExpressiveTextPresenter { Name = "ScenarioExpressiveText" };
        tree.Root.AddChild(presenter);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        var settings = new LocalSettingsSave
        {
            ModernUiMotion = true,
            ReducedMotion = false,
        };
        presenter.Present(
            "scenario:line",
            "Hold [input]right mouse button[/input] for a [impact]big swing[/impact].",
            settings);
        bool wasRevealing = presenter.IsRevealing;
        bool consumed = presenter.CompleteReveal();
        checks.Add(new StartupCheck(
            "expressive_first_click_completes_only_reveal",
            wasRevealing && consumed && !presenter.IsRevealing && presenter.VisibleCharacters == -1,
            $"started={wasRevealing} consumed={consumed} revealing={presenter.IsRevealing} visible={presenter.VisibleCharacters}"));
        presenter.QueueFree();

        var loaded = await M4LifecycleScenarioSupport.Load(tree, new ManualMonotonicTimeSource());
        if (loaded is null)
        {
            checks.Add(new StartupCheck("expressive_preview_sandbox_loadable", false, "sandbox"));
            return new ScenarioResult(false, checks, messages);
        }

        SandboxRoot sandbox = loaded.Value.Sandbox;
        var owner = new SubViewportContainer
        {
            Name = "ScenarioPreviewOwner",
            Visible = true,
            Size = new Vector2(160, 160),
        };
        tree.Root.AddChild(owner);

        var continuous = new BuddyPreviewSurface { Name = "ScenarioContinuousPreview" };
        continuous.Configure(
            rigName: "ScenarioContinuousRig",
            viewportSize: new Vector2I(160, 160),
            transparentBackground: true,
            rigProfile: sandbox.Buddy.Rig.Profile,
            visualProfile: sandbox.Buddy.VisualProfile,
            cameraSize: 160.0f,
            cameraPosition: new Vector3(0, 0, 600),
            lightRotationDegrees: new Vector3(-30, -20, 0),
            visibilityOwner: owner);
        owner.AddChild(continuous);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        checks.Add(new StartupCheck(
            "expressive_preview_visible_runs_continuously",
            continuous.RenderTargetUpdateMode == SubViewport.UpdateMode.Always,
            continuous.RenderTargetUpdateMode.ToString()));

        continuous.RequestSingleFrame();
        checks.Add(new StartupCheck(
            "expressive_preview_refresh_does_not_strand_continuous_surface",
            continuous.RenderTargetUpdateMode == SubViewport.UpdateMode.Always,
            continuous.RenderTargetUpdateMode.ToString()));

        owner.Visible = false;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        checks.Add(new StartupCheck(
            "expressive_preview_hidden_suspends_rendering",
            continuous.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled,
            continuous.RenderTargetUpdateMode.ToString()));

        owner.Visible = true;
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        checks.Add(new StartupCheck(
            "expressive_preview_reappearing_resumes_rendering",
            continuous.RenderTargetUpdateMode == SubViewport.UpdateMode.Always,
            continuous.RenderTargetUpdateMode.ToString()));

        var capture = new BuddyPreviewSurface { Name = "ScenarioCapturePreview" };
        capture.Configure(
            rigName: "ScenarioCaptureRig",
            viewportSize: new Vector2I(160, 160),
            transparentBackground: true,
            rigProfile: sandbox.Buddy.Rig.Profile,
            visualProfile: sandbox.Buddy.VisualProfile,
            cameraSize: 160.0f,
            cameraPosition: new Vector3(0, 0, 600),
            lightRotationDegrees: new Vector3(-30, -20, 0));
        tree.Root.AddChild(capture);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool captureStartsDisabled = capture.RenderTargetUpdateMode == SubViewport.UpdateMode.Disabled;
        capture.RequestSingleFrame();
        bool captureRequestsOnce = capture.RenderTargetUpdateMode == SubViewport.UpdateMode.Once;
        checks.Add(new StartupCheck(
            "expressive_capture_preview_is_one_shot",
            captureStartsDisabled && captureRequestsOnce,
            $"startsDisabled={captureStartsDisabled} afterRequest={capture.RenderTargetUpdateMode}"));

        bool containsGameplayAuthority = Descendants(continuous.WorldRoot).Any(static node =>
            node is PhysicsBody3D || node is CollisionObject3D ||
            node.GetType().Name.Contains("Reaction", StringComparison.Ordinal) ||
            node.GetType().Name.Contains("Autonom", StringComparison.Ordinal));
        checks.Add(new StartupCheck(
            "expressive_preview_tree_has_no_gameplay_authority",
            !containsGameplayAuthority,
            $"gameplayAuthority={containsGameplayAuthority}"));

        capture.QueueFree();
        owner.QueueFree();
        await M4LifecycleScenarioSupport.Cleanup(tree, sandbox);

        return new ScenarioResult(checks.All(static check => check.Passed), checks, messages);
    }

    private static IEnumerable<Node> Descendants(Node root)
    {
        var pending = new Stack<Node>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            Node node = pending.Pop();
            yield return node;
            foreach (Node child in node.GetChildren())
                pending.Push(child);
        }
    }
}

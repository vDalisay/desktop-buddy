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
/// live Drop Tool copy, first-click reveal completion, reduced-motion fallback, tutorial pointer
/// tracking, and the shared physics-free preview's visible/hidden/capture render lifecycle.
/// </summary>
public sealed class ExpressivePresentationScenario : IScenario
{
    public string Id => "expressive_presentation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        Vector2 portraitCenter = new(500, 400);
        // Half-extent is the display half-size at runtime; the contract is the same shape at any
        // screen size, so the check pins direction and clamping rather than a pixel window.
        Vector2 halfExtent = new(220, 180);
        Vector2 lookRight = LiveTutorialBuddyPresenter.ResolvePointerLook(
            new Vector2(720, 400), portraitCenter, halfExtent);
        Vector2 lookUp = LiveTutorialBuddyPresenter.ResolvePointerLook(
            new Vector2(500, 220), portraitCenter, halfExtent);
        Vector2 lookFarDiagonal = LiveTutorialBuddyPresenter.ResolvePointerLook(
            new Vector2(5000, 5000), portraitCenter, halfExtent);
        checks.Add(new StartupCheck(
            "expressive_tutorial_pointer_look_is_directional_and_clamped",
            lookRight.X > 0.95f && Math.Abs(lookRight.Y) < 0.001f &&
            lookUp.Y < -0.95f && Math.Abs(lookUp.X) < 0.001f &&
            lookFarDiagonal.Length() <= 1.001f,
            $"right={lookRight} up={lookUp} diagonal={lookFarDiagonal}"));

        // Exercise the real persistence seam rather than passing a pretend chord straight to copy.
        // A rebound value must survive LocalSettingsInputBindings and the semantic/plain projection.
        LocalSettingsSave reboundSettings = LocalSettingsInputBindings.WithDropTool(
            new LocalSettingsSave(),
            "Ctrl+K");
        string rebound = LocalSettingsInputBindings.DropTool(reboundSettings);
        TutorialExpressiveCopy.TryFormat(TutorialStepIds.UnequipTool, rebound, out string dropSemantic);
        IReadOnlyList<ExpressiveTextRun> dropRuns = ExpressiveSemanticMarkup.Parse(dropSemantic);
        string dropPlainText = ExpressiveSemanticMarkup.PlainText(dropRuns);
        checks.Add(new StartupCheck(
            "expressive_drop_prompt_uses_live_binding",
            dropPlainText.Contains($"press {rebound} to drop it", StringComparison.Ordinal) &&
            !dropPlainText.Contains("press D to drop it", StringComparison.Ordinal),
            dropPlainText));

        // Authored copy is the single source of the plain prompt, so every semantic line must
        // still parse back to readable prose with no markup left in it. This is what stops a
        // stray or misspelled tag from shipping as literal text in a tutorial instruction.
        var markupLeaks = new List<string>();
        foreach (string stepId in TutorialStepIds.Ordered)
        {
            if (!TutorialExpressiveCopy.TryFormat(stepId, "D", out string semantic))
                continue;
            string plain = ExpressiveSemanticMarkup.PlainText(ExpressiveSemanticMarkup.Parse(semantic));
            if (plain.Contains('[') || plain.Contains(']') || plain.Length == 0)
                markupLeaks.Add(stepId);
        }
        checks.Add(new StartupCheck(
            "expressive_authored_copy_projects_to_clean_prose",
            markupLeaks.Count == 0,
            markupLeaks.Count == 0 ? "all authored lines clean" : string.Join(",", markupLeaks)));

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
            wasRevealing && consumed && !presenter.IsRevealing && presenter.VisibleRatio >= 0.999f,
            $"started={wasRevealing} consumed={consumed} revealing={presenter.IsRevealing} ratio={presenter.VisibleRatio:0.###}"));

        var reducedSettings = settings with { ReducedMotion = true };
        presenter.Present(
            "scenario:reduced",
            "A [playful]quiet wave[/playful] becomes static.",
            reducedSettings);
        checks.Add(new StartupCheck(
            "expressive_reduced_motion_is_immediate_static_and_silent",
            !presenter.IsRevealing && !presenter.IsSpeaking && presenter.VisibleRatio >= 0.999f &&
            !presenter.Text.Contains("[wave", StringComparison.Ordinal),
            $"revealing={presenter.IsRevealing} speaking={presenter.IsSpeaking} ratio={presenter.VisibleRatio:0.###} text={presenter.Text}"));
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

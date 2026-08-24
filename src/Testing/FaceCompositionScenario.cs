using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M3.6 Task 5 gate (`face_composition`): the composed dynamic face. Checks the plan's
/// three scenario obligations as named checks — `expression_map_coverage` (every semantic
/// face string resolves to a feature pose and the compositor is composed with the accepted
/// style), `face_semantic_roundtrip` (a real head strike produces `>_&lt;` and the
/// compositor's last-composited state is the pain pose), and `blink_suppression` (the
/// seeded blink runs while eyes are blinkable and disarms completely for closed/special-eye
/// states) — plus the change-only re-render rule and the eat chew overlay. Every assertion
/// is semantic (poses, render keys, counts); the GPU texture is never sampled.
/// </summary>
public sealed class FaceCompositionScenario : IScenario
{
    public string Id => "face_composition";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };

        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            checks.Add(new StartupCheck("face_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        await ScenarioSteps.WaitForStanding(tree, lab, 1800);

        checks.Add(CheckExpressionMapCoverage(lab));
        checks.Add(new StartupCheck("calm_default_face_is_smile",
            lab.Reactions.CurrentFace == ":)",
            $"face={lab.Reactions.CurrentFace}"));
        checks.Add(CheckEquippedMouthIsWhatACalmBuddyWears());
        checks.Add(await CheckRerenderOnChangeOnly(tree, lab, seed, messages));
        checks.Add(await CheckBlinkRunsAndSuppresses(tree, lab, seed, messages));
        checks.Add(await CheckChewOverlay(tree, lab, messages));
        // Last: the strike leaves real pain, fear, and harmful memory behind.
        checks.Add(await CheckSemanticRoundtrip(tree, lab, messages));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// Every string the reaction resolver can produce resolves to a feature pose, the
    /// authoritative list is exactly the resolver's ten, and the composed scene carries the
    /// accepted style with a live render key.
    /// </summary>
    /// <summary>
    /// The calm face is ":)" and nothing in the world produces ":|", so if the smile ignores the
    /// equipped style then every mouth in the shop looks the same on the buddy and only differs
    /// in Buddy Studio (owner report 2026-08-23). Two families are enough to prove the smile
    /// follows the style: they must differ from each other and match their own neutral mouth.
    /// The frown stays generic — a reaction has to read as itself whatever is equipped.
    /// </summary>
    private static readonly CharacterFeatureRendererRegistry Renderers = new();

    private static StartupCheck CheckEquippedMouthIsWhatACalmBuddyWears()
    {
        Vector2[] roundedSmile = MouthPath(CharacterFeatureIds.MouthRounded, FaceMouthPose.Smile);
        Vector2[] roundedNeutral = MouthPath(CharacterFeatureIds.MouthRounded, FaceMouthPose.Flat);
        Vector2[] linePath = MouthPath(CharacterFeatureIds.MouthLine, FaceMouthPose.Smile);
        Vector2[] roundedFrown = MouthPath(CharacterFeatureIds.MouthRounded, FaceMouthPose.Frown);
        Vector2[] lineFrown = MouthPath(CharacterFeatureIds.MouthLine, FaceMouthPose.Frown);

        bool smileFollowsStyle =
            roundedSmile.Length == roundedNeutral.Length &&
            roundedSmile.Length != linePath.Length &&
            roundedFrown.Length == lineFrown.Length;

        return new StartupCheck(
            "a_calm_buddy_smiles_in_the_mouth_style_it_wears",
            smileFollowsStyle,
            $"rounded_smile={roundedSmile.Length} rounded_neutral={roundedNeutral.Length} " +
            $"line_smile={linePath.Length} frowns={roundedFrown.Length}/{lineFrown.Length}");
    }

    /// <summary>The point path one equipped mouth style draws for one pose.</summary>
    private static Vector2[] MouthPath(string featureId, FaceMouthPose pose)
    {
        ICharacterMouthRenderer renderer = Renderers.Mouth(featureId);
        var appearance = new CompiledFeatureAppearance(
            featureId, NormalizedFeatureTransform.Identity, Rgba32.Parse("#183042"));
        IReadOnlyList<CharacterDrawCommand> commands =
            renderer.Build(appearance, pose, Colors.Black);
        var points = new List<Vector2>();
        foreach (CharacterDrawCommand command in commands)
            points.AddRange(command.Points);
        return points.ToArray();
    }

    private static StartupCheck CheckExpressionMapCoverage(BuddyLab lab)
    {
        bool allResolve = true;
        foreach (string face in FaceExpressionMap.Faces)
        {
            allResolve &= FaceExpressionMap.TryResolve(face, out _);
        }

        bool listComplete = FaceExpressionMap.Faces.Count == 10;
        bool composed = lab.Face.IsInitialized &&
            lab.Face.Style == FaceStyleId.SoftOval &&
            lab.Face.RenderCount > 0;
        return new StartupCheck("expression_map_coverage",
            allResolve && listComplete && composed,
            $"faces={FaceExpressionMap.Faces.Count} all_resolve={allResolve} " +
            $"style={lab.Face.Style} renders={lab.Face.RenderCount}");
    }

    /// <summary>
    /// The change-only rule, sharpened: with the blink stretched beyond the window and
    /// ambient glances stretched with it (so no pupil quantum can move), a calm idle
    /// buddy composes the identical state every frame and the render count must not move
    /// AT ALL for four hundred frames.
    /// </summary>
    private static async Task<StartupCheck> CheckRerenderOnChangeOnly(
        SceneTree tree, BuddyLab lab, ulong seed, List<string> messages)
    {
        BuddyExpressionProfile profile = lab.Face.Profile;
        (int blinkMinimum, int blinkMaximum) = (
            profile.BlinkIntervalMinimumTicks, profile.BlinkIntervalMaximumTicks);
        (int glanceMinimum, int glanceMaximum) = (
            profile.LookGlanceIntervalMinimumTicks, profile.LookGlanceIntervalMaximumTicks);
        profile.BlinkIntervalMinimumTicks = 2000;
        profile.BlinkIntervalMaximumTicks = 2400;
        profile.LookGlanceIntervalMinimumTicks = 14000;
        profile.LookGlanceIntervalMaximumTicks = 14400;
        lab.Face.Reseed(seed);
        lab.HeadLookAt.Reseed(seed);

        // Let any in-flight pupil ease settle onto its final quantum first.
        for (int frame = 0; frame < 90; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        int before = lab.Face.RenderCount;
        for (int frame = 0; frame < 400; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        int delta = lab.Face.RenderCount - before;
        profile.BlinkIntervalMinimumTicks = blinkMinimum;
        profile.BlinkIntervalMaximumTicks = blinkMaximum;
        profile.LookGlanceIntervalMinimumTicks = glanceMinimum;
        profile.LookGlanceIntervalMaximumTicks = glanceMaximum;
        lab.Face.Reseed(seed);
        lab.HeadLookAt.Reseed(seed);

        messages.Add($"rerender_gate renders_before={before} delta={delta}");
        return new StartupCheck("face_rerender_on_change_only", delta == 0,
            $"render_delta_over_400_calm_frames={delta}");
    }

    /// <summary>
    /// The plan's `blink_suppression` obligation. Compressed cadence (restored afterwards,
    /// the glance-determinism trick): a blinkable idle face must blink with lids down for
    /// the profile hold, and knocking the buddy out (`x_x`, special eyes) must disarm the
    /// blink completely — no lid ever closes while the face is unblinkable.
    /// </summary>
    private static async Task<StartupCheck> CheckBlinkRunsAndSuppresses(
        SceneTree tree, BuddyLab lab, ulong seed, List<string> messages)
    {
        BuddyExpressionProfile profile = lab.Face.Profile;
        (int savedMinimum, int savedMaximum) = (
            profile.BlinkIntervalMinimumTicks, profile.BlinkIntervalMaximumTicks);
        profile.BlinkIntervalMinimumTicks = 24;
        profile.BlinkIntervalMaximumTicks = 60;
        lab.Face.Reseed(seed);

        // Observe blinking on the calm neutral face. Holds are measured in ROUTED ticks
        // between the composed rise and fall edges — frame counting would couple the
        // check to frame:tick pacing and break under a different --fixed-fps.
        int rises = 0;
        int completedHolds = 0;
        bool holdsPlausible = true;
        long riseTick = 0;
        bool previous = lab.Face.LastComposedState.Blinking;
        for (int frame = 0; frame < 900 && completedHolds < 2; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool blinking = lab.Face.LastComposedState.Blinking;
            if (blinking && !previous)
            {
                rises++;
                riseTick = lab.Buddy.RoutedTicks;
            }
            else if (!blinking && previous)
            {
                long hold = lab.Buddy.RoutedTicks - riseTick;
                completedHolds++;
                holdsPlausible &= hold >= profile.BlinkClosedTicks - 4 &&
                    hold <= profile.BlinkClosedTicks + 6;
            }

            previous = blinking;
        }

        bool blinked = rises >= 2 && completedHolds >= 2;
        bool holdPlausible = holdsPlausible;

        // Knockout: "x_x" has special eyes, so the blink must disarm entirely. The
        // reaction face resolves on a routed physics tick and the compositor on the
        // following rendered frame, so wait for each in turn instead of trusting a fixed
        // frame count.
        lab.Buddy.SetConsciousness(Consciousness.Unconscious);
        for (int frame = 0; frame < 60 && lab.Reactions.CurrentFace != "x_x"; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }

        for (int frame = 0; frame < 60 &&
            lab.Face.LastComposedState.Eyes != FaceEyePose.Cross; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        bool knockoutFace = lab.Reactions.CurrentFace == "x_x" &&
            lab.Face.LastComposedState.Eyes == FaceEyePose.Cross;
        bool neverBlinkedOut = true;
        for (int frame = 0; frame < 300; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            neverBlinkedOut &= !lab.Face.LastComposedState.Blinking;
        }

        lab.Buddy.SetConsciousness(Consciousness.Conscious);
        profile.BlinkIntervalMinimumTicks = savedMinimum;
        profile.BlinkIntervalMaximumTicks = savedMaximum;
        lab.Face.Reseed(seed);
        for (int frame = 0; frame < 30; frame++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        messages.Add($"blink rises={rises} holds={completedHolds} ko_face={knockoutFace}");
        return new StartupCheck("blink_suppression",
            blinked && holdPlausible && knockoutFace && neverBlinkedOut,
            $"rises={rises} hold_plausible={holdPlausible} knockout_face={knockoutFace} " +
            $"never_blinked_unconscious={neverBlinkedOut}");
    }

    /// <summary>
    /// The eat overlay: while the eat activity holds a socketed item over a calm face the
    /// mouth is a chew frame and both frames appear within a cycle; ending the activity
    /// returns the semantic mouth.
    /// </summary>
    private static async Task<StartupCheck> CheckChewOverlay(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        var visual = new MeshInstance3D
        {
            Name = "ChewItemVisual",
            Mesh = new SphereMesh { Radius = 3.0f, Height = 6.0f },
        };
        lab.Activities.AttachItemVisual(visual);

        bool sawOpen = false;
        bool sawClosed = false;
        bool onlyChewMouths = true;
        for (int frame = 0; frame < 240 && !(sawOpen && sawClosed); frame++)
        {
            lab.Buddy.SetBehaviorActivity(ActivityId.Eat);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (lab.Activities.Current != ActivityId.Eat)
            {
                continue;
            }

            FaceMouthPose mouth = lab.Face.LastComposedState.Mouth;
            sawOpen |= mouth == FaceMouthPose.ChewOpen;
            sawClosed |= mouth == FaceMouthPose.ChewClosed;
            onlyChewMouths &= mouth is FaceMouthPose.ChewOpen or FaceMouthPose.ChewClosed;
        }

        lab.Buddy.SetBehaviorActivity(ActivityId.None);
        lab.Activities.ClearItemVisual();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool mouthRestored = lab.Face.LastComposedState.Mouth
            is not (FaceMouthPose.ChewOpen or FaceMouthPose.ChewClosed);

        messages.Add($"chew open={sawOpen} closed={sawClosed} restored={mouthRestored}");
        return new StartupCheck("chew_overlay_during_eat",
            sawOpen && sawClosed && onlyChewMouths && mouthRestored,
            $"open={sawOpen} closed={sawClosed} only_chew={onlyChewMouths} restored={mouthRestored}");
    }

    /// <summary>
    /// The plan's `face_semantic_roundtrip`: a real controlled strike on the head produces
    /// the semantic `>_&lt;`, and the compositor's last-composited state is the pain pose
    /// (scrunch eyes, squiggle mouth, no blink, no pupils) — the render key changed for it.
    /// </summary>
    private static async Task<StartupCheck> CheckSemanticRoundtrip(
        SceneTree tree, BuddyLab lab, List<string> messages)
    {
        int rendersBefore = lab.Face.RenderCount;
        AcceptedImpact? impact =
            await ScenarioSteps.StrikePartAtSpeed(tree, lab, lab.Buddy.Rig.Head, 2000.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool painFace = lab.Reactions.CurrentFace == ">_<";
        FaceRenderState state = lab.Face.LastComposedState;
        bool painPose = state.Eyes == FaceEyePose.Scrunch &&
            state.Brows == FaceBrowPose.AngledIn &&
            state.Mouth == FaceMouthPose.Squiggle &&
            !state.Blinking &&
            state.PupilX == 0.0f && state.PupilY == 0.0f;
        bool rerendered = lab.Face.RenderCount > rendersBefore;

        messages.Add($"roundtrip impact={impact is not null} face={lab.Reactions.CurrentFace} " +
            $"eyes={state.Eyes} renders={lab.Face.RenderCount}");
        return new StartupCheck("face_semantic_roundtrip",
            impact is not null && painFace && painPose && rerendered,
            $"impact={impact is not null} pain_face={painFace} pain_pose={painPose} " +
            $"rerendered={rerendered}");
    }
}

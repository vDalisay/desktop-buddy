using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// User-testing Show limbs gate: the editor-only pose must move the trusted hand/foot paint
/// targets to their visible spread positions and restore the original mapper exactly when disabled.
/// </summary>
public sealed class PaintLimbPoseScenario : IScenario
{
    public string Id => "paint_limb_pose_mapping";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var canvas = new PaintCanvasControl
        {
            Name = "PaintLimbPoseScenarioCanvas",
            Size = new Vector2(400, 400),
        };
        tree.Root.AddChild(canvas);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        try
        {
            Vector2 rightHandHome = CanvasPoint(38, -5);
            Vector2 rightHandSpread = CanvasPoint(78, -5);
            Vector2 leftFootHome = CanvasPoint(-22, 55);
            Vector2 leftFootSpread = CanvasPoint(-40, 55);

            bool authoredHome = canvas.PartAt(rightHandHome) == PaintPart.RightHand &&
                canvas.PartAt(leftFootHome) == PaintPart.LeftFoot;
            checks.Add(new StartupCheck(
                "paint_limb_pose_starts_at_authored_mapper_centers",
                authoredHome,
                $"right={canvas.PartAt(rightHandHome)} leftFoot={canvas.PartAt(leftFootHome)}"));

            canvas.SetExpandedLimbPose(true);
            bool spread = canvas.ExpandedLimbPose &&
                canvas.PartAt(rightHandSpread) == PaintPart.RightHand &&
                canvas.PartAt(leftFootSpread) == PaintPart.LeftFoot &&
                canvas.PartAt(rightHandHome) != PaintPart.RightHand;
            checks.Add(new StartupCheck(
                "paint_limb_pose_moves_trusted_targets_to_spread_pose",
                spread,
                $"rightSpread={canvas.PartAt(rightHandSpread)} leftFootSpread={canvas.PartAt(leftFootSpread)} rightHome={canvas.PartAt(rightHandHome)}"));

            canvas.SetExpandedLimbPose(false);
            bool restored = !canvas.ExpandedLimbPose &&
                canvas.PartAt(rightHandHome) == PaintPart.RightHand &&
                canvas.PartAt(leftFootHome) == PaintPart.LeftFoot &&
                canvas.PartAt(rightHandSpread) != PaintPart.RightHand;
            checks.Add(new StartupCheck(
                "paint_limb_pose_restores_authored_mapping_exactly",
                restored,
                $"rightHome={canvas.PartAt(rightHandHome)} leftFootHome={canvas.PartAt(leftFootHome)} rightSpread={canvas.PartAt(rightHandSpread)}"));

            bool passed = checks.TrueForAll(check => check.Passed);
            return new ScenarioResult(passed, checks, [$"seed={seed}"]);
        }
        finally
        {
            canvas.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    private static Vector2 CanvasPoint(float worldX, float worldY) =>
        new(200.0f + worldX, 200.0f + worldY);
}

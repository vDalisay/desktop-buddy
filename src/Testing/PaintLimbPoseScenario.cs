using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.UI.Win98;
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
            Vector2 rightHandHomeProbe = CanvasPoint(38, -15);
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
            Vector2 rightArmConnector = CanvasPoint(58, -4);
            Vector2 leftLegConnector = CanvasPoint(-27, 37);
            PaintHit? rightEndHit = canvas.HitAt(rightHandSpread);
            PaintHit? rightConnectorHit = canvas.HitAt(rightArmConnector);
            bool spread = canvas.ExpandedLimbPose &&
                rightEndHit is { Part: PaintPart.RightHand, IsConnector: false } && rightEndHit.Value.Uv.X < 0.5 &&
                canvas.PartAt(leftFootSpread) == PaintPart.LeftFoot &&
                canvas.PartAt(rightHandHomeProbe) != PaintPart.RightHand &&
                rightConnectorHit is { Part: PaintPart.RightHand, IsConnector: true } && rightConnectorHit.Value.Uv.X >= 0.5 &&
                canvas.PartAt(leftLegConnector) == PaintPart.LeftFoot;
            checks.Add(new StartupCheck(
                "paint_limb_pose_moves_trusted_targets_to_spread_pose",
                spread,
                $"rightSpread={canvas.PartAt(rightHandSpread)} leftFootSpread={canvas.PartAt(leftFootSpread)} " +
                $"arm={canvas.PartAt(rightArmConnector)} leg={canvas.PartAt(leftLegConnector)}"));

            canvas.SetExpandedLimbPose(false);
            bool restored = !canvas.ExpandedLimbPose &&
                canvas.PartAt(rightHandHome) == PaintPart.RightHand &&
                canvas.PartAt(rightHandHomeProbe) == PaintPart.RightHand &&
                canvas.PartAt(leftFootHome) == PaintPart.LeftFoot &&
                canvas.PartAt(rightHandSpread) != PaintPart.RightHand &&
                canvas.PartAt(rightArmConnector) is null &&
                canvas.PartAt(leftLegConnector) is null;
            checks.Add(new StartupCheck(
                "paint_limb_pose_restores_authored_mapping_exactly",
                restored,
                $"rightHome={canvas.PartAt(rightHandHome)} leftFootHome={canvas.PartAt(leftFootHome)} rightSpread={canvas.PartAt(rightHandSpread)}"));

            canvas.SelectPaintTool(PaintTool.Pen);
            canvas.Workspace.SetBrushDiameter(64);
            Vector2 torsoBoundary = CanvasPoint(0, -7);
            Click(canvas, torsoBoundary);
            bool torsoLeft = Painted(canvas, torsoBoundary + Vector2.Left * 9);
            bool torsoRight = Painted(canvas, torsoBoundary + Vector2.Right * 9);
            bool torsoUp = Painted(canvas, torsoBoundary + Vector2.Up * 9);
            bool torsoDown = Painted(canvas, torsoBoundary + Vector2.Down * 9);
            bool torsoOutside = Painted(canvas, torsoBoundary + new Vector2(11, 11));
            bool torsoCircle = torsoLeft && torsoRight && torsoUp && torsoDown && !torsoOutside;
            checks.Add(new StartupCheck(
                "paint_pen_is_screen_circular_across_torso_uv_boundary",
                torsoCircle,
                $"left={torsoLeft} right={torsoRight} up={torsoUp} down={torsoDown} outside={torsoOutside}"));

            canvas.Workspace.EraseAll();
            canvas.Workspace.SetBrushDiameter(PaintPolicy.MaxBrushDiameter);
            Vector2 torsoCenter = CanvasPoint(0, 0);
            string blankTorso = canvas.Workspace.Surfaces[PaintPart.Torso].ComputeHash();
            Click(canvas, torsoCenter);
            int torsoGaps = 0;
            for (int y = -6; y <= 6; y++)
            {
                for (int x = -12; x <= 12; x++)
                {
                    if (!Painted(canvas, torsoCenter + new Vector2(x, y)))
                        torsoGaps++;
                }
            }
            bool torsoUndo = canvas.Workspace.Undo() &&
                canvas.Workspace.Surfaces[PaintPart.Torso].ComputeHash() == blankTorso;
            bool torsoSolid = torsoGaps == 0 && torsoUndo;
            checks.Add(new StartupCheck(
                "paint_max_pen_dab_fills_torso_without_stripes",
                torsoSolid,
                $"gaps={torsoGaps} undo={torsoUndo}"));

            canvas.Workspace.EraseAll();
            canvas.Workspace.SetBrushDiameter(64);
            Click(canvas, rightHandHome);
            bool limbLeft = Painted(canvas, rightHandHome + Vector2.Left * 9);
            bool limbRight = Painted(canvas, rightHandHome + Vector2.Right * 9);
            bool limbUp = Painted(canvas, rightHandHome + Vector2.Up * 9);
            bool limbDown = Painted(canvas, rightHandHome + Vector2.Down * 9);
            bool limbOutside = Painted(canvas, rightHandHome + new Vector2(11, 11));
            bool limbCircle = limbLeft && limbRight && limbUp && limbDown && !limbOutside;
            checks.Add(new StartupCheck(
                "paint_pen_is_screen_circular_on_half_atlas_limbs",
                limbCircle,
                $"left={limbLeft} right={limbRight} up={limbUp} down={limbDown} outside={limbOutside}"));

            float maximumVisibleDiameter = PaintPolicy.MaxBrushDiameter * 400f /
                (PaintPolicy.SurfaceSize * 2f);
            int penGridRadius = PaintCanvasControl.PenSampleSteps(
                maximumVisibleDiameter,
                PaintPolicy.MaxBrushDiameter);
            int penCandidates = (penGridRadius * 2 + 1) * (penGridRadius * 2 + 1);
            int penSampleDiameter = PaintCanvasControl.PenSampleDiameter(
                maximumVisibleDiameter,
                PaintPolicy.MaxBrushDiameter,
                penGridRadius);
            long penTexelVisitBound = (long)penCandidates * penSampleDiameter * penSampleDiameter;
            float brushSpacing = PaintCanvasControl.StrokeSampleSpacing(
                PaintTool.Brush,
                maximumVisibleDiameter);
            checks.Add(new StartupCheck(
                "paint_max_brush_sampling_is_bounded",
                penCandidates <= 16384 && penTexelVisitBound <= 1_000_000 && brushSpacing >= 4f,
                $"pen_candidates={penCandidates} sample={penSampleDiameter}px " +
                $"texel_bound={penTexelVisitBound} brush_spacing={brushSpacing:0.00}px"));

            var paletteRoot = new Control();
            var paletteBootstrap = new Win98PaintCustomPaletteBootstrap();
            tree.Root.AddChild(paletteRoot);
            paletteBootstrap.EnsureEditDialog(paletteRoot);
            Control? colorEditor = paletteRoot.FindChild("PaintColorBlockEditor", true, false) as Control;
            Control? colorBlocker = paletteRoot.FindChild("PaintColorBlockModalBlocker", true, false) as Control;
            bool paletteUi = colorEditor?.ZIndex > 100 && colorBlocker?.ZIndex > 100;
            checks.Add(new StartupCheck(
                "paint_palette_modal_stays_above_view_controls",
                paletteUi,
                $"z={colorEditor?.ZIndex}/{colorBlocker?.ZIndex}/100"));
            paletteRoot.QueueFree();
            paletteBootstrap.Free();

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

    private static void Click(PaintCanvasControl canvas, Vector2 point)
    {
        canvas._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Position = point,
            Pressed = true,
        });
        canvas._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Position = point,
            Pressed = false,
        });
    }

    private static bool Painted(PaintCanvasControl canvas, Vector2 point) =>
        canvas.HitAt(point) is PaintHit hit &&
        canvas.Workspace.Surfaces[hit.Part].TrySample(hit.Uv, out _);
}

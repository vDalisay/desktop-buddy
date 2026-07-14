using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>M3.5 integration gate for the render-only frontal 3D presentation.</summary>
public sealed class Presentation3DScenario : IScenario
{
    public string Id => "presentation_3d";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 20.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("presentation_scene_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        StartupReport startup = StartupValidator.Validate(
            new GameResource[] { lab.Buddy.VisualProfile });
        bool visualProfileValid = false;
        foreach (StartupCheck check in startup.Checks)
        {
            if (check.Name == $"resource:{lab.Buddy.VisualProfile.ResourceName}")
            {
                visualProfileValid = check.Passed;
                break;
            }
        }
        checks.Add(new StartupCheck("visual_profile_valid", visualProfileValid,
            lab.Buddy.VisualProfile.ResourceName));

        bool presenterBuilt =
            lab.VisualPresenter.PartVisualCount == PuppetRigProfile.RequiredPartCount &&
            lab.VisualPresenter.ConnectorVisualCount == lab.Buddy.VisualProfile.Connectors.Count &&
            GodotObject.IsInstanceValid(lab.VisualPresenter.FaceLabel) &&
            Mathf.IsEqualApprox(
                lab.VisualPresenter.FaceLabel.PixelSize,
                lab.Buddy.VisualProfile.FacePixelSize);
        checks.Add(new StartupCheck("presenter_built", presenterBuilt,
            $"parts={lab.VisualPresenter.PartVisualCount} " +
            $"connectors={lab.VisualPresenter.ConnectorVisualCount} " +
            $"face_pixel_size={lab.VisualPresenter.FaceLabel.PixelSize:F3}"));

        lab.SetPresentationMode(PresentationMode.Mii3D);
        AcceptedImpact? faceImpact = await ScenarioSteps.StrikePart(
            tree, lab, lab.Buddy.Rig.Head);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool faceRoundtrip = faceImpact is not null &&
            lab.Reactions.CurrentFace == ">_<" &&
            lab.VisualPresenter.FaceLabel.Text == lab.Reactions.CurrentFace;
        checks.Add(new StartupCheck("face_roundtrip", faceRoundtrip,
            $"semantic={lab.Reactions.CurrentFace} label={lab.VisualPresenter.FaceLabel.Text}"));

        bool cameraAligned = await CheckCameraAlignment(tree, lab, messages);
        checks.Add(new StartupCheck("camera_alignment", cameraAligned,
            "supported zooms at 480x360 plus 700x520"));

        lab.SetPresentationMode(PresentationMode.LegacyCircles);
        bool legacyVisibility = !lab.VisualPresenter.Visible && AllPartsVisible(lab, true);
        AcceptedImpact? legacyImpact = await ScenarioSteps.StrikePart(
            tree, lab, lab.Buddy.Rig.Torso);
        lab.SetPresentationMode(PresentationMode.Mii3D);
        bool miiVisibility = lab.VisualPresenter.Visible && AllPartsVisible(lab, false);
        AcceptedImpact? miiImpact = await ScenarioSteps.StrikePart(
            tree, lab, lab.Buddy.Rig.Torso);
        bool equalPain = legacyImpact is not null && miiImpact is not null &&
            Mathf.IsEqualApprox((float)legacyImpact.Value.Pain, (float)miiImpact.Value.Pain);
        checks.Add(new StartupCheck("mode_toggle_physics_invariant",
            legacyVisibility && miiVisibility && equalPain,
            $"legacy={legacyImpact?.Pain:F3} mii3d={miiImpact?.Pain:F3}"));

        bool gloveLifecycle = await CheckGloveCounterpart(tree, lab, messages);
        checks.Add(new StartupCheck("glove_3d_spawn_pulse_despawn", gloveLifecycle,
            "dynamic attach/detach and squash parity"));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }
        return new ScenarioResult(passed, checks, messages);
    }

    private static async Task<bool> CheckCameraAlignment(
        SceneTree tree,
        BuddyLab lab,
        List<string> messages)
    {
        Camera3D? camera = lab.Boundaries.WorldCamera3D;
        if (!GodotObject.IsInstanceValid(camera))
        {
            return false;
        }

        camera!.MakeCurrent();
        float maximumError = 0.0f;
        bool aligned = true;
        foreach (double zoom in RoomLayoutPolicy.SupportedZooms)
        {
            aligned &= await ApplyLayout(tree, lab, new Vector2I(480, 360), zoom);
            maximumError = Mathf.Max(maximumError, AlignmentError(lab, camera));
        }

        aligned &= await ApplyLayout(tree, lab, new Vector2I(700, 520), 1.0);
        maximumError = Mathf.Max(maximumError, AlignmentError(lab, camera));
        messages.Add($"camera_max_error_px={maximumError:F4}");
        return aligned && maximumError < 0.5f;
    }

    private static float AlignmentError(BuddyLab lab, Camera3D camera)
    {
        RoomLayout layout = lab.Boundaries.CurrentLayout;
        Vector2 viewportSize = lab.GetViewport().GetVisibleRect().Size;
        float pixelsPerWorldUnit = viewportSize.Y / (float)layout.RoomHeight;
        Vector2 world = lab.Buddy.Rig.Torso.GlobalPosition;
        var roomCenter = new Vector2(
            (float)layout.RoomWidth * 0.5f,
            (float)layout.RoomHeight * 0.5f);
        Vector2 expected = viewportSize * 0.5f + (world - roomCenter) * pixelsPerWorldUnit;
        Vector2 actual = camera.UnprojectPosition(WorldPlaneMapping.To3D(world));
        return expected.DistanceTo(actual);
    }

    private static async Task<bool> ApplyLayout(
        SceneTree tree,
        BuddyLab lab,
        Vector2I clientSize,
        double zoom)
    {
        int targetCount = lab.Boundaries.AppliedLayoutCount + 1;
        lab.Boundaries.RequestLayout(clientSize, zoom);
        for (int tick = 0; tick < 3; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Boundaries.AppliedLayoutCount >= targetCount)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                return true;
            }
        }
        return false;
    }

    private static bool AllPartsVisible(BuddyLab lab, bool expected)
    {
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            if (part.Visible != expected)
            {
                return false;
            }
        }
        return true;
    }

    private static async Task<bool> CheckGloveCounterpart(
        SceneTree tree,
        BuddyLab lab,
        List<string> messages)
    {
        lab.SetPresentationMode(PresentationMode.Mii3D);
        lab.Glove.MoveCursor(new Vector2(80.0f, 80.0f));
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        BoxingGloveBody? glove = lab.Glove.Glove;
        bool attached = glove is not null && lab.GloveVisual.Target == glove &&
            lab.GloveVisual.Visible && !glove.Visible;
        if (glove is null)
        {
            return false;
        }

        glove.PulseImpact(Vector2.Right, 1.0f, 1.0);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Vector2 expectedScale = glove.VisualScale2D;
        Vector3 actualScale = lab.GloveVisual.Mesh.Scale;
        float scaleError = new Vector2(actualScale.X, actualScale.Y).DistanceTo(expectedScale);
        float expectedAngle = WorldPlaneMapping.To3DRotationZ(
            glove.GlobalRotation + glove.VisualRotation2D);
        float angleError = Mathf.Abs(Mathf.AngleDifference(
            lab.GloveVisual.GlobalRotation.Z, expectedAngle));

        lab.Pipeline.SelectTool(ToolId.Pet);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool detached = !lab.GloveVisual.IsAttached && !lab.GloveVisual.Visible;
        messages.Add($"glove_attached={attached} detached={detached} " +
            $"scale_error={scaleError:F4} angle_error={angleError:F4}");
        return attached && scaleError < 0.01f && angleError < 0.01f && detached;
    }
}

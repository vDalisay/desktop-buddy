using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Presentation;
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

        // Task 5: the composed face plate replaced the Label3D glyph in composed scenes.
        bool presenterBuilt =
            lab.VisualPresenter.PartVisualCount == PuppetRigProfile.RequiredPartCount &&
            lab.VisualPresenter.ConnectorVisualCount == lab.Buddy.VisualProfile.Connectors.Count &&
            GodotObject.IsInstanceValid(lab.VisualPresenter.FacePlate) &&
            lab.Face.IsInitialized;
        checks.Add(new StartupCheck("presenter_built", presenterBuilt,
            $"parts={lab.VisualPresenter.PartVisualCount} " +
            $"connectors={lab.VisualPresenter.ConnectorVisualCount} " +
            $"face_plate={GodotObject.IsInstanceValid(lab.VisualPresenter.FacePlate)}"));

        checks.Add(CheckYawOwnsHandDepthSorting(lab, messages));

        lab.SetPresentationMode(PresentationMode.Mii3D);
        AcceptedImpact? faceImpact = await ScenarioSteps.StrikePart(
            tree, lab, lab.Buddy.Rig.Head);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool faceRoundtrip = faceImpact is not null &&
            lab.Reactions.CurrentFace == ">_<" &&
            lab.Face.LastComposedState.Eyes == FaceEyePose.Scrunch &&
            lab.Face.LastComposedState.Mouth == FaceMouthPose.Squiggle;
        checks.Add(new StartupCheck("face_roundtrip", faceRoundtrip,
            $"semantic={lab.Reactions.CurrentFace} composed_eyes={lab.Face.LastComposedState.Eyes}"));

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

        BatVisualProbe batVisual = await CheckBatCounterpart(tree, lab);
        checks.Add(new StartupCheck(
            "lathed_bat_stays_inside_the_authoritative_capsule",
            batVisual.IsLathed &&
            batVisual.VertexCount > 0 &&
            batVisual.AllVerticesInsideCapsule,
            $"lathed={batVisual.IsLathed} vertices={batVisual.VertexCount} " +
            $"inside={batVisual.AllVerticesInsideCapsule}"));
        checks.Add(new StartupCheck(
            "bat_uses_authored_wood_and_grip_under_per_pixel_light",
            batVisual.PerPixel &&
            batVisual.RoughnessMatches &&
            batVisual.HasWoodColor &&
            batVisual.HasGripColor &&
            batVisual.BarrelAndGripEndsAligned &&
            batVisual.ShadowlessAcceptedRig,
            $"per_pixel={batVisual.PerPixel} roughness={batVisual.RoughnessMatches} " +
            $"wood={batVisual.HasWoodColor} grip={batVisual.HasGripColor} " +
            $"ends_aligned={batVisual.BarrelAndGripEndsAligned} " +
            $"shadowless_rig={batVisual.ShadowlessAcceptedRig} " +
            $"colors={batVisual.ColorCount} first={batVisual.FirstColor}"));
        checks.Add(new StartupCheck(
            "cursor_tool_root_has_only_the_generic_dynamic_visual_slot",
            batVisual.GenericSlotOnly,
            $"children={lab.CursorToolVisual.GetChildCount()} " +
            $"slot={lab.CursorToolVisual.Slot.Name}"));

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

    private static StartupCheck CheckYawOwnsHandDepthSorting(
        BuddyLab lab,
        List<string> messages)
    {
        BuddyVisualPresenter presenter = lab.VisualPresenter;
        PartVisualDefinition leftDefinition =
            lab.Buddy.VisualProfile.FindPart(BuddyPartId.LeftHand)!;
        PartVisualDefinition rightDefinition =
            lab.Buddy.VisualProfile.FindPart(BuddyPartId.RightHand)!;

        presenter.SetDevelopmentYawDegrees(0.0f);
        float leftIdentityDepth = presenter.GetPartSocket(BuddyPartId.LeftHand).GlobalPosition.Z;
        float rightIdentityDepth = presenter.GetPartSocket(BuddyPartId.RightHand).GlobalPosition.Z;
        bool identityPreserved = Mathf.Abs(leftIdentityDepth - leftDefinition.DepthOffset) < 0.001f &&
            Mathf.Abs(rightIdentityDepth - rightDefinition.DepthOffset) < 0.001f;

        float committedYaw = lab.Facing.Profile.FacingYawDegrees;
        presenter.SetDevelopmentYawDegrees(committedYaw);
        float torsoAtRight = presenter.GetPartSocket(BuddyPartId.Torso).GlobalPosition.Z;
        float rightAtRight = presenter.GetPartSocket(BuddyPartId.RightHand).GlobalPosition.Z;
        bool rightFarBehind = rightAtRight < torsoAtRight;

        presenter.SetDevelopmentYawDegrees(-committedYaw);
        float torsoAtLeft = presenter.GetPartSocket(BuddyPartId.Torso).GlobalPosition.Z;
        float leftAtLeft = presenter.GetPartSocket(BuddyPartId.LeftHand).GlobalPosition.Z;
        bool leftFarBehind = leftAtLeft < torsoAtLeft;
        presenter.SetDevelopmentYawDegrees(0.0f);

        messages.Add($"hand_depth identity={leftIdentityDepth:F2}/{rightIdentityDepth:F2} " +
            $"right_turn={rightAtRight:F2}<{torsoAtRight:F2} " +
            $"left_turn={leftAtLeft:F2}<{torsoAtLeft:F2}");
        return new StartupCheck(
            "yaw_owns_far_hand_depth_sorting",
            identityPreserved && rightFarBehind && leftFarBehind,
            $"identity={identityPreserved} right_far={rightFarBehind} left_far={leftFarBehind}");
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
        // The mapping the 3D camera must match is Camera2D's: room centre at the viewport
        // centre, one world unit per EffectiveZoom pixels. The room no longer fills the
        // viewport (frame chrome is inset out of it), so viewportHeight/RoomHeight is not it.
        float pixelsPerWorldUnit = (float)layout.EffectiveZoom;
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

    private readonly record struct BatVisualProbe(
        bool IsLathed,
        int VertexCount,
        bool AllVerticesInsideCapsule,
        bool PerPixel,
        bool RoughnessMatches,
        bool HasWoodColor,
        bool HasGripColor,
        bool BarrelAndGripEndsAligned,
        bool ShadowlessAcceptedRig,
        bool GenericSlotOnly,
        int ColorCount,
        Color FirstColor);

    private static async Task<BatVisualProbe> CheckBatCounterpart(
        SceneTree tree,
        BuddyLab lab)
    {
        lab.SetPresentationMode(PresentationMode.Mii3D);
        lab.CursorTools.MoveCursor(new Vector2(100.0f, 80.0f));
        lab.Pipeline.SelectTool(ToolId.BaseballBat);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        CursorToolProfile? profile = lab.CursorTools.ActiveProfile;
        Mesh? mesh = lab.CursorToolVisual.Mesh.Mesh;
        bool isLathed =
            profile?.Visual3DKind == CursorToolVisual3DKind.LathedBat &&
            lab.CursorToolVisual.ActiveKind == CursorToolVisual3DKind.LathedBat &&
            mesh is ArrayMesh;

        Vector3[] vertices = mesh?.GetFaces() ?? System.Array.Empty<Vector3>();
        bool inside = profile is not null;
        foreach (Vector3 vertex in vertices)
        {
            inside &= BatMeshBuilder.IsInsideCapsule(
                vertex,
                profile!.Length,
                profile.Radius);
        }

        bool hasWood = false;
        bool hasGrip = false;
        bool endsAligned = false;
        int colorCount = 0;
        Color firstColor = default;
        if (mesh is ArrayMesh arrayMesh && arrayMesh.GetSurfaceCount() > 0 && profile is not null)
        {
            Godot.Collections.Array arrays = arrayMesh.SurfaceGetArrays(0);
            Color[] colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            Vector3[] rawVertices =
                arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            colorCount = colors.Length;
            firstColor = colors.Length > 0 ? colors[0] : default;
            foreach (Color color in colors)
            {
                // SurfaceTool stores vertex colours in the mesh's 8-bit packed
                // colour channel, so compare within one quantization step.
                hasWood |= PackedColorMatches(color, profile.VisualColor);
                hasGrip |= profile.Swing is { } swing &&
                           PackedColorMatches(color, swing.GripColor);
            }

            if (rawVertices.Length == colors.Length && rawVertices.Length > 0 &&
                profile.Swing is { } authoredSwing)
            {
                float minimumY = float.PositiveInfinity;
                float maximumY = float.NegativeInfinity;
                foreach (Vector3 vertex in rawVertices)
                {
                    minimumY = Mathf.Min(minimumY, vertex.Y);
                    maximumY = Mathf.Max(maximumY, vertex.Y);
                }

                bool woodAtBarrel = false;
                bool gripAtHandle = false;
                for (int index = 0; index < rawVertices.Length; index++)
                {
                    woodAtBarrel |=
                        Mathf.Abs(rawVertices[index].Y - maximumY) <= 0.001f &&
                        PackedColorMatches(colors[index], profile.VisualColor);
                    gripAtHandle |=
                        Mathf.Abs(rawVertices[index].Y - minimumY) <= 0.001f &&
                        PackedColorMatches(colors[index], authoredSwing.GripColor);
                }

                endsAligned = woodAtBarrel && gripAtHandle;
            }
        }

        StandardMaterial3D? material =
            lab.CursorToolVisual.Mesh.MaterialOverride as StandardMaterial3D;
        bool perPixel =
            material?.ShadingMode == BaseMaterial3D.ShadingModeEnum.PerPixel &&
            material.VertexColorUseAsAlbedo;
        bool roughnessMatches =
            material is not null && Mathf.IsEqualApprox(material.Roughness, 0.7f);
        bool shadowlessRig =
            lab.LightingRig.GetChildCount() == 2 &&
            !lab.LightingRig.KeyLight.ShadowEnabled &&
            !lab.LightingRig.FillLight.ShadowEnabled;
        bool genericSlotOnly =
            lab.CursorToolVisual.GetChildCount() == 1 &&
            lab.CursorToolVisual.Slot.Name == "DynamicBodyVisualSlot" &&
            lab.CursorToolVisual.FindChild("*Bat*", recursive: true, owned: false) is null;

        return new BatVisualProbe(
            isLathed,
            vertices.Length,
            inside,
            perPixel,
            roughnessMatches,
            hasWood,
            hasGrip,
            endsAligned,
            shadowlessRig,
            genericSlotOnly,
            colorCount,
            firstColor);
    }

    private static bool PackedColorMatches(Color actual, Color expected)
    {
        const float tolerance = 1.1f / 255.0f;
        return Mathf.Abs(actual.R - expected.R) <= tolerance &&
               Mathf.Abs(actual.G - expected.G) <= tolerance &&
               Mathf.Abs(actual.B - expected.B) <= tolerance &&
               Mathf.Abs(actual.A - expected.A) <= tolerance;
    }

    private static async Task<bool> CheckGloveCounterpart(
        SceneTree tree,
        BuddyLab lab,
        List<string> messages)
    {
        lab.SetPresentationMode(PresentationMode.Mii3D);
        lab.CursorTools.MoveCursor(new Vector2(80.0f, 80.0f));
        lab.Pipeline.SelectTool(ToolId.BoxingGlove);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        CursorToolBody? glove = lab.CursorTools.Body;
        bool attached = glove is not null && lab.CursorToolVisual.Target == glove &&
            lab.CursorToolVisual.Visible && !glove.Visible &&
            lab.CursorToolVisual.ActiveKind == CursorToolVisual3DKind.Capsule &&
            lab.CursorToolVisual.Mesh.Mesh is SphereMesh;
        if (glove is null)
        {
            return false;
        }

        glove.PulseImpact(Vector2.Right, 1.0f, 1.0);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Vector2 expectedScale = glove.VisualScale2D;
        Vector3 actualScale = lab.CursorToolVisual.Mesh.Scale;
        float scaleError = new Vector2(actualScale.X, actualScale.Y).DistanceTo(expectedScale);
        float expectedAngle = WorldPlaneMapping.To3DRotationZ(
            glove.GlobalRotation + glove.VisualRotation2D);
        float angleError = Mathf.Abs(Mathf.AngleDifference(
            lab.CursorToolVisual.GlobalRotation.Z, expectedAngle));

        lab.Pipeline.SelectTool(ToolId.Pet);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        bool detached = !lab.CursorToolVisual.IsAttached && !lab.CursorToolVisual.Visible;
        messages.Add($"glove_attached={attached} detached={detached} " +
            $"scale_error={scaleError:F4} angle_error={angleError:F4}");
        return attached && scaleError < 0.01f && angleError < 0.01f && detached;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>A2 gate for the shared gameplay/editor visual rig boundary.</summary>
public sealed class CharacterRigViewScenario : IScenario
{
    public string Id => "character_rig_view";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await ScenarioSteps.CreateControlledImpactLab(tree, 10.0f, 20.0f);
        if (lab is null)
        {
            checks.Add(new StartupCheck("a2_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        var source = new StaticBuddyVisualTransformSource(
            lab.Buddy.Rig.Profile,
            new Vector2(240.0f, 180.0f));
        var preview = new BuddyVisualRigView
        {
            Name = "A2PhysicsFreePreview",
        };
        lab.AddChild(preview);
        preview.Initialize(lab.Buddy.VisualProfile, source);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        checks.Add(CheckStaticSource(lab, source));
        int physicsAuthorities = CountPhysicsAuthorities(preview);
        checks.Add(new StartupCheck(
            "a2_preview_has_no_physics_authority",
            physicsAuthorities == 0,
            $"physics_authorities={physicsAuthorities}"));
        checks.Add(new StartupCheck(
            "a2_preview_builds_without_buddy_root",
            preview.IsInitialized &&
            preview.PartVisualCount == PuppetRigProfile.RequiredPartCount &&
            preview.ConnectorVisualCount == lab.Buddy.VisualProfile.Connectors.Count,
            $"parts={preview.PartVisualCount} connectors={preview.ConnectorVisualCount}"));

        bool underlaysNull = true;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
            underlaysNull &= preview.SurfaceUnderlay((BuddyPartId)index) is null;
        checks.Add(new StartupCheck(
            "a2_surface_underlays_remain_null",
            underlaysNull,
            "all six Phase A underlay slots"));

        BuddyVisualRigTrustSnapshot trust = preview.CaptureTrustSnapshot();
        CompiledCharacterAppearance first = Appearance(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            new Rgba32(220, 70, 80),
            new Rgba32(40, 110, 180));
        CompiledCharacterAppearance second = Appearance(
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            new Rgba32(60, 210, 120),
            new Rgba32(160, 80, 220));

        preview.ApplyAppearance(first);
        bool firstApplied = Approximately(
            preview.ActiveBasePartAlbedo(BuddyPartId.Head),
            ToGodotColor(first.PartColors.Head));
        preview.ApplyAppearance(second);
        bool secondApplied = Approximately(
            preview.ActiveBasePartAlbedo(BuddyPartId.Head),
            ToGodotColor(second.PartColors.Head));
        bool geometryUnchanged = preview.TrustedGeometryMatches(trust);
        checks.Add(new StartupCheck(
            "a2_appearance_changes_only_visual_bases",
            firstApplied && secondApplied && geometryUnchanged,
            $"first={firstApplied} second={secondApplied} trust={geometryUnchanged}"));

        ImageTexture paintUnderlay = ImageTexture.CreateFromImage(
            Image.CreateEmpty(2, 2, false, Image.Format.Rgba8));
        preview.SetSurfaceUnderlay(BuddyPartId.Torso, paintUnderlay);
        preview.SetSurfaceUnderlay(BuddyPartId.LeftHand, paintUnderlay);
        preview.SetSurfaceUnderlay(BuddyPartId.RightHand, paintUnderlay);
        preview.SetSurfaceUnderlay(BuddyPartId.LeftFoot, paintUnderlay);
        preview.SetSurfaceUnderlay(BuddyPartId.RightFoot, paintUnderlay);
        int paintedConnectors = 0;
        for (int index = 0; index < preview.ConnectorVisualCount; index++)
        {
            if (preview.GetConnectorVisual(index).FindChild("Paint", false, false) is MeshInstance3D
                {
                    Visible: true,
                    MaterialOverride: StandardMaterial3D { AlbedoTexture: not null },
                })
                paintedConnectors++;
        }
        checks.Add(new StartupCheck(
            "painted_hand_and_foot_surfaces_bind_to_four_limb_connectors",
            paintedConnectors == 4,
            $"paintedConnectors={paintedConnectors}"));
        MeshInstance3D leftHandPaint = (MeshInstance3D)preview.GetPartSocket(BuddyPartId.LeftHand)
            .FindChild("Paint", false, false);
        MeshInstance3D leftArmPaint = (MeshInstance3D)preview.GetConnectorVisual(1)
            .FindChild("Paint", false, false);
        bool limbAtlasSplit = leftHandPaint.MaterialOverride is StandardMaterial3D
            {
                Uv1Scale: { X: 0.5f },
                Uv1Offset: { X: 0.0f },
            } && leftArmPaint.MaterialOverride is StandardMaterial3D
            {
                Uv1Scale: { X: 0.5f },
                Uv1Offset: { X: 0.5f },
            };
        checks.Add(new StartupCheck(
            "limb_end_and_connector_sample_disjoint_halves_of_one_surface",
            limbAtlasSplit,
            $"end={leftHandPaint.MaterialOverride} connector={leftArmPaint.MaterialOverride}"));
        MeshInstance3D? trustedFacePlate = preview.FacePlate;
        MeshInstance3D trustedAccentPlate = preview.TorsoAccentPlate;
        var attachmentColor = new Rgba32(110, 72, 54);
        CompiledCharacterAppearance attachments = second with
        {
            Hair = new CompiledFeatureAppearance(
                CharacterFeatureIds.HairShortSweep,
                NormalizedFeatureTransform.Identity,
                attachmentColor),
            Glasses = new CompiledFeatureAppearance(
                CharacterFeatureIds.GlassesWorkClassic,
                new NormalizedFeatureTransform(0.2, -0.15, 1.1),
                new Rgba32(24, 48, 66)),
            Headwear = new CompiledFeatureAppearance(
                CharacterFeatureIds.HeadwearSoftCap,
                NormalizedFeatureTransform.Identity,
                new Rgba32(201, 91, 99)),
            Nose = new CompiledFeatureAppearance(
                CharacterFeatureIds.NoseButton,
                new NormalizedFeatureTransform(0, 0.08, 0.9),
                new Rgba32(240, 160, 107)),
            Ears = new CompiledFeatureAppearance(
                CharacterFeatureIds.EarsRoundTabs,
                NormalizedFeatureTransform.Identity,
                new Rgba32(116, 185, 232)),
            Tops = new CompiledFeatureAppearance(
                CharacterFeatureIds.TopUtilityBib,
                NormalizedFeatureTransform.Identity,
                new Rgba32(227, 163, 58)),
            Shoes = new CompiledFeatureAppearance(
                CharacterFeatureIds.ShoesSoftSteps,
                NormalizedFeatureTransform.Identity,
                new Rgba32(90, 101, 117)),
        };
        preview.ApplyAppearance(attachments);
        Node3D? hair = preview.GetCosmeticVisual(CharacterFeatureSlot.Hair);
        Node3D? nose = preview.GetCosmeticVisual(CharacterFeatureSlot.Nose);
        Node3D? leftEar = preview.GetCosmeticVisual(CharacterFeatureSlot.Ears);
        Node3D? rightEar = preview.GetPairedCosmeticVisual(CharacterFeatureSlot.Ears);
        Node3D? glasses = preview.GetCosmeticVisual(CharacterFeatureSlot.Glasses);
        Node3D? headwear = preview.GetCosmeticVisual(CharacterFeatureSlot.Headwear);
        Node3D? top = preview.GetCosmeticVisual(CharacterFeatureSlot.Tops);
        Node3D? leftShoe = preview.GetCosmeticVisual(CharacterFeatureSlot.Shoes);
        Node3D? rightShoe = preview.GetPairedCosmeticVisual(CharacterFeatureSlot.Shoes);
        bool anchorsComplete = Enum.GetValues<BuddyCosmeticAnchorId>().All(anchor =>
            GodotObject.IsInstanceValid(preview.GetCosmeticAnchor(anchor)));
        bool attachmentsVisualOnly =
            GodotObject.IsInstanceValid(hair) &&
            GodotObject.IsInstanceValid(nose) &&
            GodotObject.IsInstanceValid(leftEar) &&
            GodotObject.IsInstanceValid(rightEar) &&
            GodotObject.IsInstanceValid(glasses) &&
            GodotObject.IsInstanceValid(headwear) &&
            GodotObject.IsInstanceValid(top) &&
            GodotObject.IsInstanceValid(leftShoe) &&
            GodotObject.IsInstanceValid(rightShoe) &&
            CountPhysicsAuthorities(preview) == 0 &&
            preview.TrustedGeometryMatches(trust);
        checks.Add(new StartupCheck(
            "bs1_trusted_anchors_and_visual_only_attachments",
            anchorsComplete && attachmentsVisualOnly,
            $"anchors={anchorsComplete} visual_only={attachmentsVisualOnly}"));
        bool pairedAnchors =
            ReferenceEquals(leftEar?.GetParent(), preview.GetCosmeticAnchor(BuddyCosmeticAnchorId.LeftEar)) &&
            ReferenceEquals(rightEar?.GetParent(), preview.GetCosmeticAnchor(BuddyCosmeticAnchorId.RightEar)) &&
            ReferenceEquals(leftShoe?.GetParent(), preview.GetCosmeticAnchor(BuddyCosmeticAnchorId.LeftFoot)) &&
            ReferenceEquals(rightShoe?.GetParent(), preview.GetCosmeticAnchor(BuddyCosmeticAnchorId.RightFoot));
        checks.Add(new StartupCheck(
            "bs3_paired_families_use_trusted_anchors",
            pairedAnchors,
            $"ears={leftEar?.GetParent()?.Name}/{rightEar?.GetParent()?.Name} shoes={leftShoe?.GetParent()?.Name}/{rightShoe?.GetParent()?.Name}"));
        int faceDetailMeshes = 0;
        int topMeshes = 0;
        int shoeMeshes = 0;
        bool remainingLayers =
            nose is not null && UsesRenderLayer(nose, BuddyCosmeticRenderLayer.FaceDetail, ref faceDetailMeshes) &&
            leftEar is not null && UsesRenderLayer(leftEar, BuddyCosmeticRenderLayer.FaceDetail, ref faceDetailMeshes) &&
            rightEar is not null && UsesRenderLayer(rightEar, BuddyCosmeticRenderLayer.FaceDetail, ref faceDetailMeshes) &&
            top is not null && UsesRenderLayer(top, BuddyCosmeticRenderLayer.Top, ref topMeshes) &&
            leftShoe is not null && UsesRenderLayer(leftShoe, BuddyCosmeticRenderLayer.Top, ref shoeMeshes) &&
            rightShoe is not null && UsesRenderLayer(rightShoe, BuddyCosmeticRenderLayer.Top, ref shoeMeshes) &&
            faceDetailMeshes == 3 && topMeshes == 3 && shoeMeshes == 2;
        checks.Add(new StartupCheck(
            "bs3_remaining_families_have_explicit_layers",
            remainingLayers,
            $"face/top/shoes={faceDetailMeshes}/{topMeshes}/{shoeMeshes}"));
        bool establishedLayersPreserved =
            ReferenceEquals(preview.FacePlate, trustedFacePlate) &&
            ReferenceEquals(preview.TorsoAccentPlate, trustedAccentPlate) &&
            ReferenceEquals(preview.SurfaceUnderlay(BuddyPartId.Torso), paintUnderlay);
        checks.Add(new StartupCheck(
            "bs3_face_accent_and_paint_layers_remain_authoritative",
            establishedLayersPreserved,
            $"face={ReferenceEquals(preview.FacePlate, trustedFacePlate)} accent={ReferenceEquals(preview.TorsoAccentPlate, trustedAccentPlate)} paint={ReferenceEquals(preview.SurfaceUnderlay(BuddyPartId.Torso), paintUnderlay)}"));
        int hairMeshes = 0;
        int glassesMeshes = 0;
        int headwearMeshes = 0;
        bool layersOrdered =
            hair is not null && UsesRenderLayer(hair, BuddyCosmeticRenderLayer.Hair, ref hairMeshes) &&
            glasses is not null && UsesRenderLayer(glasses, BuddyCosmeticRenderLayer.Glasses, ref glassesMeshes) &&
            headwear is not null && UsesRenderLayer(headwear, BuddyCosmeticRenderLayer.Headwear, ref headwearMeshes) &&
            hairMeshes > 0 && glassesMeshes > 0 && headwearMeshes > 0 &&
            BuddyCosmeticRenderLayer.Hair < BuddyCosmeticRenderLayer.Glasses &&
            BuddyCosmeticRenderLayer.Glasses < BuddyCosmeticRenderLayer.Headwear;
        checks.Add(new StartupCheck(
            "bs1_explicit_attachment_layer_order",
            layersOrdered,
            $"ordered={layersOrdered} meshes={hairMeshes}/{glassesMeshes}/{headwearMeshes}"));
        checks.Add(new StartupCheck(
            "bs1_headwear_hides_hair_without_deleting_selection",
            hair is { Visible: false } &&
            preview.ActiveAppearance?.Hair.ResolvedFeatureId == CharacterFeatureIds.HairShortSweep,
            $"hair_visible={hair?.Visible} selected={preview.ActiveAppearance?.Hair.ResolvedFeatureId}"));

        CompiledCharacterAppearance capRemoved = attachments with
        {
            Headwear = new CompiledFeatureAppearance(
                CharacterFeatureIds.HeadwearNone,
                NormalizedFeatureTransform.Identity,
                new CompiledColorChannels([])),
        };
        preview.ApplyAppearance(capRemoved);
        checks.Add(new StartupCheck(
            "bs1_removing_headwear_restores_saved_hair",
            preview.GetCosmeticVisual(CharacterFeatureSlot.Hair) is { Visible: true } restoredHair &&
            ReferenceEquals(restoredHair, hair),
            $"same_hair={ReferenceEquals(preview.GetCosmeticVisual(CharacterFeatureSlot.Hair), hair)}"));

        var visualCatalog = new BuddyCosmeticVisualCatalog();
        BuddyCosmeticVisualDefinition fallback = visualCatalog.Resolve(
            CharacterFeatureSlot.Hair,
            "hair.future",
            out bool usedFallback);
        checks.Add(new StartupCheck(
            "bs1_missing_visual_falls_back_safely",
            usedFallback && fallback.CosmeticId == CharacterFeatureIds.HairNone &&
            fallback.Kind == BuddyCosmeticVisualKind.None,
            $"fallback={fallback.CosmeticId} kind={fallback.Kind}"));

        preview.SetPartScorch(BuddyPartId.Head, 0.5f, Colors.Black);
        CompiledCharacterAppearance scorchedSwap = first with
        {
            CharacterId = Guid.Parse("33333333-3333-4333-8333-333333333333"),
            PartColors = first.PartColors with { Head = new Rgba32(30, 120, 240) },
        };
        preview.ApplyAppearance(scorchedSwap);
        Color customBase = ToGodotColor(scorchedSwap.PartColors.Head);
        Color expectedScorched = customBase.Lerp(Colors.Black, 0.5f);
        bool scorchSurvived =
            Mathf.IsEqualApprox(preview.PartScorchAmount(BuddyPartId.Head), 0.5f) &&
            Approximately(preview.PartAlbedo(BuddyPartId.Head), expectedScorched);
        preview.SetPartScorch(BuddyPartId.Head, 0.0f, Colors.Black);
        bool fadedToCustomBase = Approximately(
            preview.PartAlbedo(BuddyPartId.Head),
            customBase);
        checks.Add(new StartupCheck(
            "a2_scorch_survives_swap_and_fades_to_custom_base",
            scorchSurvived && fadedToCustomBase,
            $"survived={scorchSurvived} faded={fadedToCustomBase}"));

        long appearanceMutations = preview.AppearanceMutationCount;
        long materialMutations = preview.PartMaterialMutationCount;
        preview.ApplyAppearance(scorchedSwap);
        bool equalNoOp =
            preview.AppearanceMutationCount == appearanceMutations &&
            preview.PartMaterialMutationCount == materialMutations;
        checks.Add(new StartupCheck(
            "a2_equal_appearance_is_noop",
            equalNoOp,
            $"appearance={appearanceMutations}->{preview.AppearanceMutationCount} " +
            $"materials={materialMutations}->{preview.PartMaterialMutationCount}"));

        long frameCountBefore = BuddyVisualPoseFrame.CreatedCount;
        lab.VisualPresenter.SetDevelopmentYawDegrees(12.0f);
        BuddyVisualPresenterSamplingSnapshot sampling =
            lab.VisualPresenter.CaptureSamplingSnapshot();
        bool frameRouted = BuddyVisualPoseFrame.CreatedCount > frameCountBefore &&
            lab.VisualPresenter.RigView.GeometrySource is LiveBuddyVisualTransformSource &&
            ReferenceEquals(sampling.Buddy, lab.Buddy) &&
            ReferenceEquals(sampling.PosePipeline, lab.VisualPresenter.PosePipeline) &&
            ReferenceEquals(sampling.Facing, lab.VisualPresenter.Facing) &&
            ReferenceEquals(sampling.Activities, lab.VisualPresenter.Activities) &&
            ReferenceEquals(sampling.HeadLookAt, lab.VisualPresenter.HeadLookAt) &&
            ReferenceEquals(sampling.ImpactVisualOffset, lab.VisualPresenter.ImpactVisualOffset);
        checks.Add(new StartupCheck(
            "a2_presenter_routes_resolved_pose_frames",
            frameRouted,
            $"frames={frameCountBefore}->{BuddyVisualPoseFrame.CreatedCount} " +
            $"yaw={Mathf.RadToDeg(sampling.BodyYawRadians):F2}"));
        lab.VisualPresenter.SetDevelopmentYawDegrees(0.0f);

        checks.Add(new StartupCheck(
            "a2_existing_presenter_oracles_preserved",
            ReferenceEquals(lab.VisualPresenter.RigView.TrustedProfile, lab.Buddy.VisualProfile) &&
            lab.VisualPresenter.PartVisualCount == PuppetRigProfile.RequiredPartCount &&
            lab.VisualPresenter.ConnectorVisualCount == lab.Buddy.VisualProfile.Connectors.Count &&
            GodotObject.IsInstanceValid(lab.VisualPresenter.FacePlate),
            $"parts={lab.VisualPresenter.PartVisualCount} " +
            $"connectors={lab.VisualPresenter.ConnectorVisualCount}"));

        preview.QueueFree();
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static StartupCheck CheckStaticSource(
        BuddyLab lab,
        StaticBuddyVisualTransformSource source)
    {
        bool matches = ReferenceEquals(source.TrustedRigProfile, lab.Buddy.Rig.Profile);
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            BuddyPartId id = (BuddyPartId)index;
            PuppetPartDefinition definition = lab.Buddy.Rig.Profile.FindPart(id)!;
            BuddyVisualTransform transform = source.ReadTransform(id);
            matches &= Mathf.IsEqualApprox(source.ReadRadius(id), definition.Radius);
            matches &= transform.Position.IsEqualApprox(source.Origin + definition.RestPosition);
            matches &= Mathf.IsZeroApprox(transform.Rotation);
            matches &= transform.LinearVelocity == Vector2.Zero;
        }

        return new StartupCheck(
            "a2_static_source_uses_trusted_rest_anatomy",
            matches,
            $"profile={source.TrustedRigProfile.ResourceName} origin={source.Origin}");
    }

    private static int CountPhysicsAuthorities(Node node)
    {
        int count = node is CollisionObject2D or Joint2D or BuddyRoot or PuppetRig ? 1 : 0;
        foreach (Node child in node.GetChildren())
            count += CountPhysicsAuthorities(child);
        return count;
    }

    private static bool UsesRenderLayer(
        Node node,
        BuddyCosmeticRenderLayer expected,
        ref int meshCount)
    {
        bool matches = true;
        if (node is MeshInstance3D mesh)
        {
            meshCount++;
            matches = mesh.MaterialOverride?.RenderPriority == (int)expected;
        }

        foreach (Node child in node.GetChildren())
            matches &= UsesRenderLayer(child, expected, ref meshCount);
        return matches;
    }

    private static CompiledCharacterAppearance Appearance(
        Guid id,
        Rgba32 head,
        Rgba32 torso)
    {
        var featureColor = new Rgba32(24, 48, 66);
        var transform = NormalizedFeatureTransform.Identity;
        return new CompiledCharacterAppearance(
            id,
            new PartColorSet(
                head,
                torso,
                new Rgba32(120, 180, 230),
                new Rgba32(120, 180, 230),
                new Rgba32(80, 150, 205),
                new Rgba32(80, 150, 205)),
            new CompiledFeatureAppearance(
                CharacterFeatureIds.EyesSoftOval,
                transform,
                featureColor),
            new CompiledFeatureAppearance(
                CharacterFeatureIds.BrowsSoftArc,
                transform,
                featureColor),
            new CompiledFeatureAppearance(
                CharacterFeatureIds.MouthRounded,
                transform,
                featureColor),
            new CompiledFeatureAppearance(
                CharacterFeatureIds.AccentNone,
                transform,
                featureColor));
    }

    private static Color ToGodotColor(Rgba32 color) => new(
        color.R / 255.0f,
        color.G / 255.0f,
        color.B / 255.0f,
        1.0f);

    private static bool Approximately(Color actual, Color expected) =>
        actual.IsEqualApprox(expected);
}

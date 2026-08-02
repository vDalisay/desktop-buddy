using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
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
        checks.Add(new StartupCheck(
            "a2_appearance_changes_only_visual_bases",
            firstApplied && secondApplied && preview.TrustedGeometryMatches(trust),
            $"first={firstApplied} second={secondApplied} trust={preview.TrustedGeometryMatches(trust)}"));

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
            ReferenceEquals(lab.VisualPresenter.RigView.GeometrySource,
                lab.VisualPresenter.RigView.GeometrySource) &&
            ReferenceEquals(sampling.Buddy, lab.Buddy) &&
            sampling.Facing == lab.VisualPresenter.Facing &&
            sampling.Activities == lab.VisualPresenter.Activities &&
            sampling.HeadLookAt == lab.VisualPresenter.HeadLookAt;
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

using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// M3.5 production-look gate: soft-toon materials, the transparent-safe two-light rig, the six
/// inverted-hull outlines, and the yaw-before-lane camera-space depth contract, plus the
/// mode/yaw/look physics invariance and an idle soak. Structural checks run on a controlled
/// lab; the soak runs on a fresh idling lab (M3_5_MATERIALS_AND_LOOK_PLAN.md L5).
/// </summary>
public sealed class PresentationLookScenario : IScenario
{
    private const float ScenarioYawDegrees = 30.0f;
    private const int SoakSampleStride = 500;

    public string Id => "presentation_look";

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

        lab.SetPresentationMode(PresentationMode.Mii3D);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        BuddyLookProfile look = lab.Buddy.VisualProfile.Look;

        checks.Add(CheckLookProfileValid(lab, look));
        checks.Add(CheckSoftToonMaterialContract(lab, look));
        checks.Add(CheckTransparentSafeLightContract(lab, look));
        checks.Add(CheckOutlineContract(lab, look));
        checks.Add(await CheckCameraSpaceDepthLane(tree, lab, messages));
        checks.Add(await CheckLookTogglePhysicsInvariant(tree, lab, messages));

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        checks.Add(await CheckLookIdleSoak(tree, seed, messages));

        bool passed = true;
        foreach (StartupCheck check in checks)
        {
            passed &= check.Passed;
        }

        return new ScenarioResult(passed, checks, messages);
    }

    private static StartupCheck CheckLookProfileValid(BuddyLab lab, BuddyLookProfile look)
    {
        bool acceptedValid = look.Validate().Count == 0 &&
            lab.Buddy.VisualProfile.Validate().Count == 0;

        bool nanEnergyFails = ContainsError(
            new BuddyLookProfile { KeyEnergy = float.NaN }.Validate(), "key light energy");
        bool negativeGrowFails = ContainsError(
            new BuddyLookProfile { OutlineGrowAmount = -1.0f }.Validate(), "outline grow");
        bool shadowsFail = ContainsError(
            new BuddyLookProfile { ShadowsEnabled = true }.Validate(), "shadows");
        bool missingLookFails = ContainsError(
            new BuddyVisualProfile().Validate(), "look profile is required");

        bool passed = acceptedValid && nanEnergyFails && negativeGrowFails &&
            shadowsFail && missingLookFails;
        return new StartupCheck("look_profile_valid", passed,
            $"accepted={acceptedValid} nan_energy={nanEnergyFails} " +
            $"neg_grow={negativeGrowFails} shadows={shadowsFail} missing_look={missingLookFails}");
    }

    private static StartupCheck CheckSoftToonMaterialContract(BuddyLab lab, BuddyLookProfile look)
    {
        BuddyVisualPresenter presenter = lab.VisualPresenter;
        BuddyVisualProfile profile = lab.Buddy.VisualProfile;
        bool passed = true;
        var detail = new List<string>();

        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            var id = (BuddyPartId)index;
            PartVisualDefinition definition = profile.FindPart(id)!;
            bool ok = IsAcceptedLitMaterial(
                presenter.GetPartMesh(id).MaterialOverride as StandardMaterial3D,
                definition.Color, look);
            if (!ok)
            {
                detail.Add($"part:{id}");
            }

            passed &= ok;
        }

        for (int index = 0; index < profile.Connectors.Count; index++)
        {
            ConnectorVisualDefinition connector = profile.Connectors[index];
            var mesh = presenter.GetConnectorVisual(index) as MeshInstance3D;
            bool ok = IsAcceptedLitMaterial(
                mesh?.MaterialOverride as StandardMaterial3D, connector.Color, look);
            if (!ok)
            {
                detail.Add($"connector:{index}");
            }

            passed &= ok;
        }

        // Every mesh owns its material instance, even at equal albedo: a future per-part
        // tint (damage flash, editor recolor) must never bleed across parts or connectors
        // through a shared instance. The hands share a colour; the torso shares the
        // connector colour — both pairs must still be distinct instances.
        bool isolated = !ReferenceEquals(
                presenter.GetPartMesh(BuddyPartId.LeftHand).MaterialOverride,
                presenter.GetPartMesh(BuddyPartId.RightHand).MaterialOverride) &&
            !ReferenceEquals(
                presenter.GetPartMesh(BuddyPartId.Torso).MaterialOverride,
                (presenter.GetConnectorVisual(0) as MeshInstance3D)?.MaterialOverride);
        passed &= isolated;

        return new StartupCheck("soft_toon_material_contract", passed,
            detail.Count == 0
                ? $"6 parts + {profile.Connectors.Count} connectors lit; per_mesh_isolated={isolated}"
                : string.Join(",", detail));
    }

    private static StartupCheck CheckTransparentSafeLightContract(BuddyLab lab, BuddyLookProfile look)
    {
        BuddyLookLightingRig rig = lab.LightingRig;
        int directionalLights =
            lab.FindChildren("*", nameof(DirectionalLight3D), recursive: true, owned: false).Count;
        int worldEnvironments =
            lab.FindChildren("*", nameof(WorldEnvironment), recursive: true, owned: false).Count;
        bool cameraNoEnvironment = GodotObject.IsInstanceValid(lab.Boundaries.WorldCamera3D) &&
            lab.Boundaries.WorldCamera3D!.Environment is null;

        bool keyOk = GodotObject.IsInstanceValid(rig.KeyLight) &&
            !rig.KeyLight.ShadowEnabled &&
            Mathf.IsEqualApprox(rig.KeyLight.LightEnergy, look.KeyEnergy) &&
            rig.KeyLight.LightColor.IsEqualApprox(look.KeyColor);
        bool fillOk = GodotObject.IsInstanceValid(rig.FillLight) &&
            !rig.FillLight.ShadowEnabled &&
            Mathf.IsEqualApprox(rig.FillLight.LightEnergy, look.FillEnergy) &&
            rig.FillLight.LightColor.IsEqualApprox(look.FillColor);

        bool passed = directionalLights == 2 && worldEnvironments == 0 &&
            cameraNoEnvironment && keyOk && fillOk;
        return new StartupCheck("transparent_safe_light_contract", passed,
            $"lights={directionalLights} environments={worldEnvironments} " +
            $"camera_env_null={cameraNoEnvironment} key_ok={keyOk} fill_ok={fillOk}");
    }

    private static StartupCheck CheckOutlineContract(BuddyLab lab, BuddyLookProfile look)
    {
        BuddyVisualPresenter presenter = lab.VisualPresenter;
        StandardMaterial3D outlineMaterial = presenter.OutlineMaterial;
        bool materialOk = outlineMaterial is not null &&
            outlineMaterial.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded &&
            outlineMaterial.CullMode == BaseMaterial3D.CullModeEnum.Front &&
            outlineMaterial.Grow &&
            Mathf.IsEqualApprox(outlineMaterial.GrowAmount, look.OutlineGrowAmount) &&
            outlineMaterial.AlbedoColor.IsEqualApprox(look.OutlineColor);

        int shells = 0;
        bool allShared = true;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            MeshInstance3D outline = presenter.GetPartOutline((BuddyPartId)index);
            if (GodotObject.IsInstanceValid(outline))
            {
                shells++;
                allShared &= ReferenceEquals(outline.MaterialOverride, outlineMaterial);
            }
        }

        // Connectors carry no outline shell (leaf mesh instances, no children).
        bool connectorsUnoutlined = true;
        for (int index = 0; index < lab.Buddy.VisualProfile.Connectors.Count; index++)
        {
            var connector = presenter.GetConnectorVisual(index) as MeshInstance3D;
            connectorsUnoutlined &= connector is not null && connector.GetChildCount() == 0;
        }

        bool passed = shells == PuppetRigProfile.RequiredPartCount && allShared &&
            materialOk && connectorsUnoutlined;
        return new StartupCheck("outline_contract", passed,
            $"shells={shells} shared={allShared} material_ok={materialOk} " +
            $"connectors_unoutlined={connectorsUnoutlined}");
    }

    private static async Task<StartupCheck> CheckCameraSpaceDepthLane(
        SceneTree tree,
        BuddyLab lab,
        List<string> messages)
    {
        Camera3D? camera = lab.Boundaries.WorldCamera3D;
        if (!GodotObject.IsInstanceValid(camera))
        {
            return new StartupCheck("camera_space_depth_lane", false, "no world camera");
        }

        camera!.MakeCurrent();
        BuddyVisualPresenter presenter = lab.VisualPresenter;
        float headDepth = lab.Buddy.VisualProfile.FindPart(BuddyPartId.Head)!.DepthOffset;

        // Independent oracle: re-derive the expected socket transform here, in scenario math,
        // from the same rendered 2D inputs the presenter consumes. The transform contract —
        // yaw about the vertical axis through the TORSO pivot, then a pure global camera-axis
        // Z lane — is encoded a second time below, so a presenter regression in pivot part,
        // yaw axis, or sign now fails instead of moving oracle and implementation together.
        static Vector3 ExpectedNoLane(BuddyVisualPresenter presenter, float yawDegrees)
        {
            Vector3 head = WorldPlaneMapping.To3D(presenter.RenderedPosition2D(BuddyPartId.Head));
            if (yawDegrees == 0.0f)
            {
                return head;
            }

            Vector3 pivot = WorldPlaneMapping.To3D(presenter.RenderedPosition2D(BuddyPartId.Torso));
            return pivot + new Basis(Vector3.Up, Mathf.DegToRad(yawDegrees)) * (head - pivot);
        }

        // Identity yaw must reproduce the current projection exactly: the socket is the mapped
        // pose with a pure global-Z DepthOffset lane.
        presenter.SetDevelopmentYawDegrees(0.0f);
        Vector3 identityExpected = ExpectedNoLane(presenter, 0.0f);
        Vector3 identitySocket = presenter.GetPartSocket(BuddyPartId.Head).GlobalPosition;
        float identityError = identitySocket.DistanceTo(identityExpected + new Vector3(0.0f, 0.0f, headDepth));

        float maxScreenXError = 0.0f;
        float maxLaneError = 0.0f;
        foreach (float yaw in new[] { ScenarioYawDegrees, -ScenarioYawDegrees })
        {
            // SetDevelopmentYawDegrees re-renders synchronously, so the sockets and the
            // rendered 2D inputs sampled below belong to the same resolved pose — no frame
            // await, no physics tick between oracle inputs and presenter outputs.
            presenter.SetDevelopmentYawDegrees(yaw);

            Vector3 expectedNoLane = ExpectedNoLane(presenter, yaw);
            Vector3 withLane = presenter.GetPartSocket(BuddyPartId.Head).GlobalPosition;
            // The socket must be exactly the yawed pose plus a global camera-axis Z lane.
            maxLaneError = Mathf.Max(maxLaneError,
                withLane.DistanceTo(expectedNoLane + new Vector3(0.0f, 0.0f, headDepth)));
            // And that lane must not shift the projected screen-X.
            float screenXError = Mathf.Abs(
                camera.UnprojectPosition(withLane).X - camera.UnprojectPosition(expectedNoLane).X);
            maxScreenXError = Mathf.Max(maxScreenXError, screenXError);
        }

        presenter.SetDevelopmentYawDegrees(0.0f);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = identityError < 0.01f && maxLaneError < 0.01f && maxScreenXError < 0.5f;
        messages.Add($"lane_identity_error={identityError:F4} lane_global_z_error={maxLaneError:F4} " +
            $"lane_screen_x_error_px={maxScreenXError:F4}");
        return new StartupCheck("camera_space_depth_lane", passed,
            $"identity={identityError:F4} global_z={maxLaneError:F4} screen_x_px={maxScreenXError:F4}");
    }

    private static async Task<StartupCheck> CheckLookTogglePhysicsInvariant(
        SceneTree tree,
        BuddyLab lab,
        List<string> messages)
    {
        // Run on the single existing controlled lab: a second lab would place a second buddy
        // and room bounds at the same world coordinates in the shared 2D physics space, so the
        // two rigs would collide and corrupt each other's contacts.
        lab.SetPresentationMode(PresentationMode.LegacyCircles);
        lab.VisualPresenter.SetDevelopmentYawDegrees(0.0f);
        AcceptedImpact? legacyImpact = await ScenarioSteps.StrikePart(tree, lab, lab.Buddy.Rig.Torso);

        // The yaw drive touches only the read-only visual sockets: a body transform must be
        // byte-identical across a yaw change with no physics step between.
        Vector2 torsoBefore = lab.Buddy.Rig.Torso.GlobalPosition;
        lab.SetPresentationMode(PresentationMode.Mii3D);
        lab.VisualPresenter.SetDevelopmentYawDegrees(ScenarioYawDegrees);
        bool bodyUnmovedByYaw = lab.Buddy.Rig.Torso.GlobalPosition == torsoBefore;

        AcceptedImpact? lookImpact = await ScenarioSteps.StrikePart(tree, lab, lab.Buddy.Rig.Torso);
        lab.VisualPresenter.SetDevelopmentYawDegrees(0.0f);

        bool equalPain = legacyImpact is not null && lookImpact is not null &&
            Mathf.IsEqualApprox((float)legacyImpact.Value.Pain, (float)lookImpact.Value.Pain);

        bool passed = equalPain && bodyUnmovedByYaw;
        messages.Add($"legacy_pain={legacyImpact?.Pain:F3} look_pain={lookImpact?.Pain:F3} " +
            $"body_unmoved_by_yaw={bodyUnmovedByYaw}");
        return new StartupCheck("look_toggle_physics_invariant", passed,
            $"equal_pain={equalPain} body_unmoved_by_yaw={bodyUnmovedByYaw}");
    }

    private static async Task<StartupCheck> CheckLookIdleSoak(
        SceneTree tree,
        ulong seed,
        List<string> messages)
    {
        BuddyLab lab = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn").Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        lab.SetPresentationMode(PresentationMode.Mii3D);
        BuddyVisualPresenter presenter = lab.VisualPresenter;

        int expectedParts = presenter.PartVisualCount;
        int expectedConnectors = presenter.ConnectorVisualCount;
        var initialPartMaterials = new Godot.Rid[PuppetRigProfile.RequiredPartCount];
        StandardMaterial3D initialOutline = presenter.OutlineMaterial;
        for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
        {
            initialPartMaterials[index] = ((StandardMaterial3D)
                presenter.GetPartMesh((BuddyPartId)index).MaterialOverride).GetRid();
        }

        // The canonical soak loop (per-tick physics finiteness, containment, hard-recovery
        // accounting) comes from SoakProbe; this check only layers the look-specific visual
        // sampling on top instead of re-implementing a weaker soak.
        bool finite = true;
        bool countsStable = true;
        bool materialsStable = true;
        int sampled = 0;
        SoakProbeResult result = await SoakProbe.RunAsync(tree, lab, IdleSoakScenario.CiTicks,
            tick =>
            {
                if (tick % SoakSampleStride != 0 || !(finite && countsStable && materialsStable))
                {
                    return;
                }

                sampled++;
                countsStable &= presenter.PartVisualCount == expectedParts &&
                    presenter.ConnectorVisualCount == expectedConnectors;
                for (int index = 0; index < PuppetRigProfile.RequiredPartCount; index++)
                {
                    var id = (BuddyPartId)index;
                    finite &= presenter.GetPartSocket(id).GlobalPosition.IsFinite();
                    var material = presenter.GetPartMesh(id).MaterialOverride as StandardMaterial3D;
                    materialsStable &= material is not null &&
                        material.GetRid() == initialPartMaterials[index];
                }

                materialsStable &= ReferenceEquals(presenter.OutlineMaterial, initialOutline);
                for (int index = 0; index < expectedConnectors; index++)
                {
                    finite &= ((Node3D)presenter.GetConnectorVisual(index)).GlobalPosition.IsFinite();
                }
            });

        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        bool passed = finite && countsStable && materialsStable &&
            result.Finite && result.Contained && result.HardRecoveries == 0;
        messages.Add($"soak_ticks={result.TickCount} sampled={sampled} finite={finite} " +
            $"counts_stable={countsStable} materials_stable={materialsStable} " +
            $"bodies_finite={result.Finite} contained={result.Contained} " +
            $"hard_recoveries={result.HardRecoveries}");
        return new StartupCheck("look_idle_soak", passed,
            $"ticks={result.TickCount} finite={finite} counts_stable={countsStable} " +
            $"materials_stable={materialsStable} bodies_finite={result.Finite} " +
            $"contained={result.Contained} hard_recoveries={result.HardRecoveries}");
    }

    private static bool IsAcceptedLitMaterial(
        StandardMaterial3D? material,
        Color expectedAlbedo,
        BuddyLookProfile look) =>
        material is not null &&
        material.ShadingMode == BaseMaterial3D.ShadingModeEnum.PerPixel &&
        material.DiffuseMode == look.DiffuseMode &&
        material.SpecularMode == look.SpecularMode &&
        Mathf.IsEqualApprox(material.MetallicSpecular, look.Specular) &&
        Mathf.IsEqualApprox(material.Roughness, look.Roughness) &&
        material.AlbedoColor.IsEqualApprox(expectedAlbedo);

    private static bool ContainsError(Godot.Collections.Array<string> errors, string fragment)
    {
        foreach (string error in errors)
        {
            if (error.Contains(fragment))
            {
                return true;
            }
        }

        return false;
    }

}

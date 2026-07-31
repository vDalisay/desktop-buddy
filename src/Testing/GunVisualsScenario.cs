using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The drawn gun (plan Task E). Everything here is presentation, and the point of every
/// check is that the presentation cannot lie about the gameplay underneath it: the barrel
/// the player sees points where the shot goes, it never stands on its head, and rounds are
/// born at the visible muzzle rather than at some unrelated authored offset.
///
/// <para>Run in <b>both</b> presentation modes. The 3D presenter and the legacy 2D drawing
/// are two views of one weapon, so the same three questions are asked of whichever one is
/// on screen — read out of the real node transform in 3D, and out of the component's own
/// drawing geometry in legacy.</para>
/// </summary>
public sealed class GunVisualsScenario : IScenario
{
    public string Id => "gun_visuals";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("gun_visual_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        CursorGunComponent gun = lab.CursorGuns;
        CursorGunVisual3D visual = lab.CursorGunVisual;
        bool is3D = lab.Mode == PresentationMode.Mii3D;
        messages.Add($"presentation={lab.Mode}");

        Rect2 room = lab.Boundaries.InnerBounds;
        var bench = new Vector2(room.GetCenter().X, room.Position.Y + 60.0f);

        // --- The mesh is inside its authored envelope, for both guns ---
        // The bat's rule, restated for a box: presentation may not reach further than the
        // dimensions the data says the weapon has.
        var envelopeReport = new List<string>();
        bool envelopeHolds = true;
        foreach (GunProfile? profile in gun.Profiles)
        {
            if (!GodotObject.IsInstanceValid(profile))
                continue;

            Aabb envelope = GunMeshBuilder.Envelope(profile!);
            ArrayMesh mesh = GunMeshBuilder.Build(profile!);
            int outside = 0;
            Vector3[] faces = mesh.GetFaces();
            foreach (Vector3 vertex in faces)
            {
                if (!GunMeshBuilder.IsInsideEnvelope(vertex, envelope))
                    outside++;
            }

            envelopeHolds &= outside == 0 && faces.Length > 0;
            envelopeReport.Add(
                $"{profile!.ContentId}: {faces.Length} verts, {outside} outside, " +
                $"kind={profile.Visual3DKind}, length={profile.VisualLengthPx}px");
        }

        checks.Add(new StartupCheck(
            "gun_meshes_stay_inside_their_authored_envelope",
            envelopeHolds,
            string.Join(" | ", envelopeReport)));

        // --- Facing: right, then left ---
        await SelectAndAim(tree, lab, gun, ToolId.Pistol, bench, Vector2.Right);
        Sighting right = Read(lab, gun, visual, is3D);
        await SelectAndAim(tree, lab, gun, ToolId.Pistol, bench, Vector2.Left);
        Sighting left = Read(lab, gun, visual, is3D);

        checks.Add(new StartupCheck(
            "gun_visual_faces_the_slewed_aim",
            right.Shown &&
            left.Shown &&
            right.Forward.Dot(right.Aim) > 0.999f &&
            left.Forward.Dot(left.Aim) > 0.999f &&
            right.Forward.Dot(Vector2.Right) > 0.99f &&
            left.Forward.Dot(Vector2.Left) > 0.99f,
            $"right_forward={right.Forward} vs aim {right.Aim} | " +
            $"left_forward={left.Forward} vs aim {left.Aim} | " +
            $"shown={right.Shown}/{left.Shown}"));

        // Screen Y grows downward, so a gun the right way up hangs its grip at positive Y
        // whichever way it points. Rotating a side-on gun past vertical instead of
        // mirroring it is exactly what this check exists to catch.
        checks.Add(new StartupCheck(
            "gun_is_never_upside_down",
            right.GripDown.Y > 0.5f && left.GripDown.Y > 0.5f,
            $"right_grip={right.GripDown} left_grip={left.GripDown} " +
            $"mirrored_right={right.Mirrored} mirrored_left={left.Mirrored}"));

        // --- Rounds leave the barrel the player can see ---
        // Both guns, because the muzzle is authored per profile and the agreement between
        // the drawn barrel and the launch point is the thing being checked.
        var muzzleReport = new List<string>();
        bool bornAtMuzzle = true;
        foreach (ToolId tool in new[] { ToolId.NerfBlaster, ToolId.Pistol })
        {
            await SelectAndAim(tree, lab, gun, tool, bench, Vector2.Right);
            Sighting sighting = Read(lab, gun, visual, is3D);
            gun.SetTriggerHeld(true);
            await Tick(tree);
            gun.SetTriggerHeld(false);

            await Tick(tree);
            ProjectileBody? shot = M4ObjectScenarioSupport.NewestLiveProjectile(gun);
            // The body's own record of where it was born, rather than its current position
            // walked backwards by however many steps have been integrated since.
            Vector2 launch = shot?.LaunchPosition ?? Vector2.Zero;
            float gap = shot is null ? float.MaxValue : launch.DistanceTo(sighting.Muzzle);
            bornAtMuzzle &= shot is not null && sighting.Shown && gap <= MuzzleTolerancePx;
            muzzleReport.Add(
                $"{tool}: launch={launch} drawn_muzzle={sighting.Muzzle} gap={gap:F2}px");
            await Idle(tree, gun, 40);
        }

        checks.Add(new StartupCheck(
            "rounds_are_born_at_the_visible_muzzle",
            bornAtMuzzle,
            $"tolerance={MuzzleTolerancePx}px | " + string.Join(" | ", muzzleReport)));

        // --- The two modes agree about where the gun is ---
        // Whichever mode this run is in, the component's own drawing geometry is the
        // number the other mode is built to match, so a divergence shows up here rather
        // than as a barrel that only lines up in one presentation.
        await SelectAndAim(tree, lab, gun, ToolId.Pistol, bench, Vector2.Right);
        Vector2 componentMuzzle = gun.VisualMuzzle2D;
        Vector2 presenterMuzzle = visual.MuzzlePoint2D;
        float modeGap = is3D ? componentMuzzle.DistanceTo(presenterMuzzle) : 0.0f;
        checks.Add(new StartupCheck(
            "both_presentations_put_the_muzzle_in_one_place",
            componentMuzzle != Vector2.Zero &&
            gun.DrawsLegacyGun == !is3D &&
            visual.Visible == is3D &&
            modeGap <= MuzzleTolerancePx,
            $"mode={lab.Mode} component={componentMuzzle} presenter={presenterMuzzle} " +
            $"gap={modeGap:F2}px legacy_draw={gun.DrawsLegacyGun} presenter_visible={visual.Visible}"));

        messages.Add(string.Join(" | ", muzzleReport));
        lab.QueueFree();
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    /// <summary>
    /// How far a round may be born from the drawn barrel mouth. The authored agreement
    /// rule allows two pixels between the launch offset and the mesh tip; this adds a
    /// pixel for the one physics step the shot has already flown when it is measured.
    /// </summary>
    private const float MuzzleTolerancePx = 3.0f;

    private static async Task Tick(SceneTree tree) =>
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

    private static async Task Idle(SceneTree tree, CursorGunComponent gun, int ticks)
    {
        gun.SetTriggerHeld(false);
        for (int tick = 0; tick < ticks; tick++)
            await Tick(tree);
    }

    private static async Task SelectAndAim(
        SceneTree tree,
        BuddyLab lab,
        CursorGunComponent gun,
        ToolId tool,
        Vector2 cursor,
        Vector2 direction)
    {
        lab.Pipeline.SelectTool(tool);
        await Tick(tree);
        await M4ObjectScenarioSupport.AimGunOver(tree, gun, cursor, direction);
        // The 3D presenter follows on the render frame, not the physics one.
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// What the drawn gun currently is, read from whichever presentation is on screen.
    /// In 3D that is the presenter's real transform; in legacy it is the geometry the
    /// component's own <c>_Draw</c> is built from.
    /// </summary>
    private static Sighting Read(
        BuddyLab lab,
        CursorGunComponent gun,
        CursorGunVisual3D visual,
        bool is3D)
    {
        if (is3D)
        {
            return new Sighting(
                visual.Visible,
                visual.Forward2D,
                visual.GripDirection2D,
                visual.MuzzlePoint2D,
                visual.IsMirrored,
                gun.AimForward.Normalized());
        }

        Vector2 forward = gun.AimForward;
        Vector2 down = new Vector2(-forward.Y, forward.X) * (forward.X < 0.0f ? -1.0f : 1.0f);
        return new Sighting(
            gun.DrawsLegacyGun && gun.IsActive && forward != Vector2.Zero,
            forward,
            down,
            gun.VisualMuzzle2D,
            forward.X < 0.0f,
            forward);
    }

    private readonly record struct Sighting(
        bool Shown,
        Vector2 Forward,
        Vector2 GripDown,
        Vector2 Muzzle,
        bool Mirrored,
        Vector2 Aim);
}

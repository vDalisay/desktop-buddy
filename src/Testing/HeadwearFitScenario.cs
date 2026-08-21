using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Where every hat actually sits on the head, measured rather than eyeballed. Hats have been
/// wrong twice: first riding forward onto the buddy's face because the crown anchor leans
/// 0.48 radii ahead of the head, then sitting as squashed ovals rather than round crowns
/// (owner reports 2026-08-21).
///
/// <para>Bounds are reported in head radii, in the head socket's own space, so the numbers
/// mean something: <c>0</c> is the middle of the head and <c>1</c> is its surface. The rules
/// a hat has to satisfy are the ones an eye applies instantly and a unit test never did —
/// centred left/right, centred front/back, wide enough to cover the skull, and sitting on the
/// head rather than hovering over it or sinking through it.</para>
/// </summary>
public sealed class HeadwearFitScenario : IScenario
{
    public string Id => "headwear_fit";

    private static readonly string[] Hats =
    [
        CharacterFeatureIds.HeadwearSoftCap,
        CharacterFeatureIds.HeadwearKnitBeanie,
        CharacterFeatureIds.HeadwearWideBrim,
        CharacterFeatureIds.HeadwearBallCap,
        CharacterFeatureIds.HeadwearSunHat,
        CharacterFeatureIds.HeadwearFedora,
    ];

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        BuddyLab? lab = await M4ObjectScenarioSupport.LoadLab(tree, seed);
        if (lab is null)
        {
            checks.Add(new StartupCheck("headwear_lab_loadable", false, "buddy_lab"));
            return new ScenarioResult(false, checks, messages);
        }

        var source = new StaticBuddyVisualTransformSource(
            lab.Buddy.Rig.Profile,
            new Vector2(240.0f, 180.0f));
        var preview = new BuddyVisualRigView { Name = "HeadwearFitPreview" };
        lab.AddChild(preview);
        preview.Initialize(lab.Buddy.VisualProfile, source);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        float headRadius = preview.PartMeshRadius(BuddyPartId.Head);
        Node3D headSocket = preview.GetPartSocket(BuddyPartId.Head);

        foreach (string hat in Hats)
        {
            preview.ApplyAppearance(AppearanceWith(hat));
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            Node3D? visual = preview.GetCosmeticVisual(CharacterFeatureSlot.Headwear);
            if (!GodotObject.IsInstanceValid(visual))
            {
                checks.Add(new StartupCheck($"{Slug(hat)}_sits_on_the_head", false, "no visual"));
                continue;
            }

            Aabb bounds = HeadLocalBounds(visual!, headSocket, headRadius);
            Vector3 centre = bounds.GetCenter();
            float halfWidth = bounds.Size.X * 0.5f;
            float top = bounds.End.Y;
            float bottom = bounds.Position.Y;

            // Centred on the head's own axis, not on the anchor that leans forward.
            bool centredSideways = Mathf.Abs(centre.X) <= 0.10f;
            bool centredDepth = Mathf.Abs(centre.Z) <= 0.35f;
            // Wide enough to cover the skull rather than perching on it.
            bool coversTheSkull = halfWidth >= 0.95f;
            // Sitting on the head: the crown clears it without towering, and the hat stops
            // above the brow line at 0.23 radii rather than coming down over his face
            // (owner report 2026-08-21).
            bool restsOnTheHead = top >= 1.05f && top <= 1.45f && bottom >= 0.30f && bottom <= 0.55f;

            checks.Add(new StartupCheck(
                $"{Slug(hat)}_sits_on_the_head",
                centredSideways && centredDepth && coversTheSkull && restsOnTheHead,
                $"centre=({centre.X:F2},{centre.Y:F2},{centre.Z:F2}) " +
                $"half_width={halfWidth:F2} top={top:F2} bottom={bottom:F2} " +
                $"sideways={centredSideways} depth={centredDepth} " +
                $"covers={coversTheSkull} rests={restsOnTheHead}"));
        }

        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }

    private static string Slug(string featureId) => featureId.Replace("headwear.", string.Empty);

    /// <summary>
    /// The visual's bounds in the head socket's own frame, expressed in head radii so
    /// <c>1.0</c> is the head's surface whatever the authored rig scale happens to be.
    /// </summary>
    private static Aabb HeadLocalBounds(Node3D visual, Node3D headSocket, float headRadius)
    {
        Transform3D toHead = headSocket.GlobalTransform.AffineInverse();
        var accumulated = new Aabb();
        bool any = false;
        foreach (MeshInstance3D mesh in visual.FindChildren("*", nameof(MeshInstance3D), true, false)
                     .OfType<MeshInstance3D>())
        {
            Aabb local = mesh.GetAabb();
            Transform3D toHeadFromMesh = toHead * mesh.GlobalTransform;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = (toHeadFromMesh * local.GetEndpoint(corner)) / headRadius;
                if (!any)
                {
                    accumulated = new Aabb(point, Vector3.Zero);
                    any = true;
                }
                else
                {
                    accumulated = accumulated.Expand(point);
                }
            }
        }

        return accumulated;
    }

    private static CompiledCharacterAppearance AppearanceWith(string headwearId)
    {
        var ink = new Rgba32(24, 48, 66);
        NormalizedFeatureTransform identity = NormalizedFeatureTransform.Identity;
        var appearance = new CompiledCharacterAppearance(
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            new PartColorSet(
                new Rgba32(120, 180, 230),
                new Rgba32(120, 180, 230),
                new Rgba32(120, 180, 230),
                new Rgba32(120, 180, 230),
                new Rgba32(80, 150, 205),
                new Rgba32(80, 150, 205)),
            new CompiledFeatureAppearance(CharacterFeatureIds.EyesSoftOval, identity, ink),
            new CompiledFeatureAppearance(CharacterFeatureIds.BrowsSoftArc, identity, ink),
            new CompiledFeatureAppearance(CharacterFeatureIds.MouthRounded, identity, ink),
            new CompiledFeatureAppearance(CharacterFeatureIds.AccentNone, identity, ink));
        return appearance with
        {
            Headwear = new CompiledFeatureAppearance(headwearId, identity, new Rgba32(201, 91, 99)),
        };
    }
}

using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The cartoon knockout ring: five flat stars orbiting over a knocked-out buddy's head, the
/// way an old short draws a character seeing stars (owner instruction 2026-08-22).
///
/// <para>Render-only. It reads <see cref="BuddyRoot.CurrentConsciousness"/> and the head part's
/// position and writes nothing back — no pain, mood, recovery or physics lane is touched. The
/// stars are built once at initialize and only shown or hidden afterwards.</para>
///
/// <para>FR-017.3: Reduced Motion parks the ring still rather than removing it, so the state is
/// still readable, and Reduced Particles thins it to the two leading stars.</para>
/// </summary>
[GlobalClass]
public partial class KnockoutStarsVisual3D : Node3D
{
    private const int StarCount = 5;
    private const float OrbitsPerSecond = 0.55f;
    private const float SpinsPerSecond = 0.30f;

    private readonly List<MeshInstance3D> _stars = new(StarCount);

    private BuddyRoot _buddy = null!;
    private EffectsSettings _settings = EffectsSettings.Default;
    private bool _presentationActive;
    private float _depthOffset;
    private double _seconds;

    public bool IsInitialized { get; private set; }

    /// <summary>Stars actually drawn this frame — the scenario-visible oracle.</summary>
    public int VisibleStarCount { get; private set; }

    public void Initialize(BuddyRoot buddy, float depthOffset = 2.0f)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(buddy);
        _buddy = buddy;
        _depthOffset = depthOffset;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        ArrayMesh mesh = BuildStarMesh();
        for (int index = 0; index < StarCount; index++)
        {
            var star = new MeshInstance3D
            {
                Name = $"KnockoutStar_{index}",
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            AddChild(star);
            _stars.Add(star);
        }

        Visible = false;
        IsInitialized = true;
    }

    public void ApplyEffectsSettings(EffectsSettings settings) => _settings = settings;

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        if (!active)
            HideAll();
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_presentationActive ||
            !GodotObject.IsInstanceValid(_buddy) ||
            _buddy.CurrentConsciousness != Consciousness.Unconscious)
        {
            HideAll();
            return;
        }

        PuppetPartBody? head = FindHead();
        if (head is null)
        {
            HideAll();
            return;
        }

        // Reduced Motion holds the ring still: the state still reads, nothing sweeps.
        if (!_settings.ReducedMotion)
            _seconds += delta;

        float radius = head.Radius;
        Vector3 centre = WorldPlaneMapping.To3D(head.GlobalPosition) +
            new Vector3(0.0f, radius * 1.45f, 0.0f);
        centre.Z = _depthOffset;

        int stride = Math.Max(1, _settings.ParticleStride);
        int visible = 0;
        for (int index = 0; index < _stars.Count; index++)
        {
            MeshInstance3D star = _stars[index];
            if (index % stride != 0)
            {
                star.Visible = false;
                continue;
            }

            float phase = (float)(_seconds * OrbitsPerSecond) + (index / (float)StarCount);
            float angle = phase * Mathf.Tau;
            // A flattened circle, so the ring reads as a plate above the head rather than a
            // vertical hoop, and the far half of the sweep rides a little higher.
            var offset = new Vector3(
                Mathf.Cos(angle) * radius * 1.05f,
                Mathf.Sin(angle) * radius * 0.30f,
                0.0f);
            star.GlobalPosition = centre + offset;
            star.Rotation = new Vector3(0.0f, 0.0f, (float)(_seconds * SpinsPerSecond * Mathf.Tau));
            float depthScale = 0.80f + (0.20f * Mathf.Sin(angle));
            float size = radius * 0.42f * depthScale;
            star.Scale = new Vector3(size, size, 1.0f);
            star.Visible = true;
            visible++;
        }

        VisibleStarCount = visible;
        Visible = visible > 0;
    }

    private void HideAll()
    {
        for (int index = 0; index < _stars.Count; index++)
            _stars[index].Visible = false;
        VisibleStarCount = 0;
        Visible = false;
    }

    private PuppetPartBody? FindHead()
    {
        IReadOnlyList<PuppetPartBody> parts = _buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
            if (parts[index].PartId == BuddyPartId.Head)
                return parts[index];
        return null;
    }

    /// <summary>
    /// A five-pointed star as one unshaded triangle fan, unit sized. Original project
    /// geometry, built here rather than imported so it scales with the head at any resolution.
    /// </summary>
    private static ArrayMesh BuildStarMesh()
    {
        const int points = 5;
        var gold = new Color("F5C542");
        var edge = new Color("8A5A0F");
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int index = 0; index < points * 2; index++)
        {
            // Start at the top point and alternate outer tip / inner notch.
            float outerAngle = (Mathf.Pi * 0.5f) + (index * Mathf.Pi / points);
            float nextAngle = (Mathf.Pi * 0.5f) + ((index + 1) * Mathf.Pi / points);
            float thisRadius = index % 2 == 0 ? 1.0f : 0.42f;
            float nextRadius = index % 2 == 0 ? 0.42f : 1.0f;
            Vector3 a = new(Mathf.Cos(outerAngle) * thisRadius, Mathf.Sin(outerAngle) * thisRadius, 0.0f);
            Vector3 b = new(Mathf.Cos(nextAngle) * nextRadius, Mathf.Sin(nextAngle) * nextRadius, 0.0f);
            tool.SetColor(edge);
            tool.AddVertex(a);
            tool.SetColor(edge);
            tool.AddVertex(b);
            tool.SetColor(gold);
            tool.AddVertex(Vector3.Zero);
        }

        ArrayMesh mesh = tool.Commit() ??
            throw new InvalidOperationException("SurfaceTool failed to build the knockout star mesh.");
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ResourceName = "KnockoutStarMaterial",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return mesh;
    }
}

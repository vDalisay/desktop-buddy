using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The glisten behind a pleased buddy's head, after a meal or a drink goes down (owner
/// instruction 2026-08-22). Same four-point sparkle the Brush already pops on the favourite
/// spot, moved from the cursor to the head and drawn behind it, so it reads as coming from
/// around the buddy rather than from the player's hand.
///
/// <para>Render-only, and it owns no clock of its own: it shows exactly while
/// <see cref="BuddyReactionComponent.IsTreatDelighted"/> holds, which is the same counter
/// driving the smile, so the two can never disagree.</para>
///
/// <para>FR-017.3: Reduced Motion parks the sparkles still instead of removing them, and
/// Reduced Particles thins the ring through the shared stride.</para>
/// </summary>
[GlobalClass]
public partial class TreatSparklesVisual3D : Node3D
{
    private const int SparkleCount = 6;

    /// <summary>Twinkles per second. Fast enough to glitter, slow enough to read as sparkle.</summary>
    private const float TwinklesPerSecond = 1.6f;

    /// <summary>How far the ring drifts up over one twinkle, as a fraction of head radius.</summary>
    private const float RiseFraction = 0.35f;

    private readonly List<MeshInstance3D> _sparkles = new(SparkleCount);

    private BuddyRoot _buddy = null!;
    private BuddyReactionComponent _reactions = null!;
    private EffectsSettings _settings = EffectsSettings.Default;
    private bool _presentationActive;
    private float _depthOffset;
    private double _seconds;

    public bool IsInitialized { get; private set; }

    /// <summary>Sparkles actually drawn this frame — the scenario-visible oracle.</summary>
    public int VisibleSparkleCount { get; private set; }

    public void Initialize(BuddyRoot buddy, BuddyReactionComponent reactions, float depthOffset)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(buddy);
        ArgumentNullException.ThrowIfNull(reactions);
        _buddy = buddy;
        _reactions = reactions;
        _depthOffset = depthOffset;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;

        ArrayMesh mesh = BuildSparkleMesh();
        for (int index = 0; index < SparkleCount; index++)
        {
            var sparkle = new MeshInstance3D
            {
                Name = $"TreatSparkle_{index}",
                Mesh = mesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            AddChild(sparkle);
            _sparkles.Add(sparkle);
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
            !GodotObject.IsInstanceValid(_reactions) ||
            !_reactions.IsTreatDelighted)
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

        // Reduced Motion holds the glisten still: it still reads as pleased, nothing twinkles.
        if (!_settings.ReducedMotion)
            _seconds += delta;

        float radius = head.Radius;
        Vector3 centre = WorldPlaneMapping.To3D(head.GlobalPosition);
        centre.Z = _depthOffset;

        int stride = Math.Max(1, _settings.ParticleStride);
        int visible = 0;
        for (int index = 0; index < _sparkles.Count; index++)
        {
            MeshInstance3D sparkle = _sparkles[index];
            if (index % stride != 0)
            {
                sparkle.Visible = false;
                continue;
            }

            // Spread over the top half of a ring wider than the head, so every sparkle clears
            // the silhouette it is sitting behind rather than being swallowed by it.
            float spread = index / (float)(SparkleCount - 1);
            float angle = Mathf.Lerp(Mathf.Pi * 0.12f, Mathf.Pi * 0.88f, spread);

            // Each one twinkles on its own offset phase, so the ring shimmers instead of
            // pulsing as a single lamp.
            float phase = (float)(_seconds * TwinklesPerSecond) + spread;
            float twinkle = phase - Mathf.Floor(phase);
            float scale = Mathf.Sin(twinkle * Mathf.Pi);

            var offset = new Vector3(
                Mathf.Cos(angle) * radius * 1.35f,
                (Mathf.Sin(angle) * radius * 1.10f) + (twinkle * radius * RiseFraction),
                0.0f);
            sparkle.GlobalPosition = centre + offset;
            sparkle.Rotation = new Vector3(0.0f, 0.0f, angle);
            float size = radius * 0.34f * scale;
            sparkle.Scale = new Vector3(size, size, 1.0f);
            // A sparkle at zero scale is a degenerate mesh rather than a small one.
            sparkle.Visible = size > 0.01f;
            if (sparkle.Visible)
                visible++;
        }

        VisibleSparkleCount = visible;
        Visible = visible > 0;
    }

    private void HideAll()
    {
        for (int index = 0; index < _sparkles.Count; index++)
            _sparkles[index].Visible = false;
        VisibleSparkleCount = 0;
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
    /// A four-point sparkle: two long points and two short ones, drawn to a pale core, in the
    /// Brush's own favourite-spot colours so the two reactions read as the same happiness.
    /// </summary>
    private static ArrayMesh BuildSparkleMesh()
    {
        var core = new Color("FFF4A8");
        var edge = new Color("F6A623");
        var tool = new SurfaceTool();
        tool.Begin(Mesh.PrimitiveType.Triangles);
        for (int index = 0; index < 8; index++)
        {
            float thisAngle = index * Mathf.Pi * 0.25f;
            float nextAngle = (index + 1) * Mathf.Pi * 0.25f;
            // Long on the axes, short on the diagonals: the pinched four-point glint shape.
            float thisRadius = index % 2 == 0 ? 1.0f : 0.22f;
            float nextRadius = index % 2 == 0 ? 0.22f : 1.0f;
            Vector3 a = new(Mathf.Cos(thisAngle) * thisRadius, Mathf.Sin(thisAngle) * thisRadius, 0.0f);
            Vector3 b = new(Mathf.Cos(nextAngle) * nextRadius, Mathf.Sin(nextAngle) * nextRadius, 0.0f);
            tool.SetColor(edge);
            tool.AddVertex(a);
            tool.SetColor(edge);
            tool.AddVertex(b);
            tool.SetColor(core);
            tool.AddVertex(Vector3.Zero);
        }

        ArrayMesh mesh = tool.Commit() ??
            throw new InvalidOperationException("SurfaceTool failed to build the treat sparkle mesh.");
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            ResourceName = "TreatSparkleMaterial",
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        });
        return mesh;
    }
}

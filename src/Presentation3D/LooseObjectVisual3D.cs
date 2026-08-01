using System;
using System.Collections.Generic;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// Draws the loose objects that ask for a model, in the frontal 3D presentation. The Grenade
/// has its own presenter because it also owns a pin swap and a blast; this is the general one
/// for objects whose whole 3D story is "it is a shape rather than a circle".
///
/// <para>Render-only, on the standard <see cref="Body2DVisual3D"/> attach seam. A pool of
/// slots is reconciled against the registry each routed tick — objects arrive by spawn,
/// eviction, and consumption, none of which announces itself, so this is polled for the same
/// reason the grenade presenter polls what it is following.</para>
///
/// <para>Degrades to nothing: an object whose profile authors
/// <see cref="LooseObjectVisualKind.None"/> is never adopted and keeps its flat circle, and in
/// legacy presentation every slot is deactivated so <b>every</b> object is flat again. One
/// silhouette per mode, never both at once.</para>
///
/// <para>Meshes are built once per (kind, radius) pair and shared between slots, so a room
/// filling with balls does not rebuild geometry on a gameplay tick.</para>
/// </summary>
[GlobalClass]
public partial class LooseObjectVisual3D : Node3D
{
    private readonly Dictionary<int, Body2DVisual3D> _slots = new();
    private readonly List<Body2DVisual3D> _free = new();
    private readonly Dictionary<(LooseObjectVisualKind Kind, int Radius), ArrayMesh> _meshes = new();
    private readonly List<int> _stale = new();

    private LooseObjectRegistry _registry = null!;
    private StandardMaterial3D _material = null!;
    private bool _presentationActive;

    public bool IsInitialized { get; private set; }

    /// <summary>How many objects are currently drawn as meshes.</summary>
    public int DrawnCount => _slots.Count;

    /// <summary>Distinct meshes built so far, for the allocation-conscious scenarios.</summary>
    public int BuiltMeshCount => _meshes.Count;

    /// <summary>Whether this object is currently being drawn as a mesh here.</summary>
    public bool IsDrawing(int runtimeId) => _slots.ContainsKey(runtimeId);

    /// <summary>
    /// Whether this object's mesh is actually on screen. Verification pairs it with the flat
    /// body's own visibility to prove exactly one silhouette is drawn per presentation mode.
    /// </summary>
    public bool MeshVisible(int runtimeId) =>
        _slots.TryGetValue(runtimeId, out Body2DVisual3D? slot) && slot.Visible;

    /// <summary>The mesh this object is drawn with, for an envelope check.</summary>
    public Mesh? MeshFor(int runtimeId) =>
        _slots.TryGetValue(runtimeId, out Body2DVisual3D? slot) ? slot.Mesh.Mesh : null;

    public void Initialize(LooseObjectRegistry registry)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(registry);
        if (!registry.IsInitialized)
            throw new ArgumentException("The loose-object visual requires a live registry.", nameof(registry));

        _registry = registry;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _material = new StandardMaterial3D
        {
            ResourceName = "ProvisionalLooseObjectMaterial",
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
            Roughness = 0.65f,
            Metallic = 0.0f,
        };
        Visible = false;
        IsInitialized = true;
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        if (!IsInitialized)
            return;

        Visible = active;
        foreach (Body2DVisual3D slot in _slots.Values)
            slot.SetPresentationActive(active);
        foreach (Body2DVisual3D slot in _free)
            slot.SetPresentationActive(active);
    }

    public void CaptureTickSnapshot()
    {
        if (!IsInitialized)
            return;

        foreach (Body2DVisual3D slot in _slots.Values)
            slot.CaptureTickSnapshot();
    }

    /// <summary>
    /// Reconciles the drawn set against the registry on the owning root's routed tick: adopt
    /// anything new that authors a shape, release anything that has left the room.
    /// </summary>
    public void PhysicsTick()
    {
        if (!IsInitialized)
            return;

        _stale.Clear();
        foreach ((int runtimeId, Body2DVisual3D slot) in _slots)
        {
            LooseObjectBody? body = _registry.FindBody(runtimeId);
            if (!GodotObject.IsInstanceValid(body) || !slot.IsAttached)
                _stale.Add(runtimeId);
        }

        foreach (int runtimeId in _stale)
            Release(runtimeId);

        for (int index = 0; index < LooseObjectRegistry.Capacity; index++)
        {
            LooseObjectBody? body = _registry.BodyAt(index);
            if (!GodotObject.IsInstanceValid(body) || _slots.ContainsKey(body!.RuntimeId))
                continue;

            LooseObjectProfile? profile = body.Profile;
            if (!GodotObject.IsInstanceValid(profile) ||
                profile!.Visual3D == LooseObjectVisualKind.None)
            {
                continue;
            }

            Adopt(body, profile);
        }
    }

    /// <summary>Drops every drawn mesh; used by hard reposition and lab clears.</summary>
    public void Reset()
    {
        if (!IsInitialized)
            return;

        _stale.Clear();
        foreach (int runtimeId in _slots.Keys)
            _stale.Add(runtimeId);
        foreach (int runtimeId in _stale)
            Release(runtimeId);
    }

    private void Adopt(LooseObjectBody body, LooseObjectProfile profile)
    {
        Body2DVisual3D slot = Rent(profile);
        slot.SetVisual(MeshFor(profile), _material, profile.VisualDepthOffset);
        slot.Attach(body);
        slot.SetPresentationActive(_presentationActive);
        _slots[body.RuntimeId] = slot;
    }

    private void Release(int runtimeId)
    {
        if (!_slots.Remove(runtimeId, out Body2DVisual3D? slot))
            return;

        LooseObjectBody? body = _registry.FindBody(runtimeId);
        if (GodotObject.IsInstanceValid(body))
            slot.Detach(body!);
        else
            slot.DetachAny();

        slot.SetPresentationActive(false);
        _free.Add(slot);
    }

    private Body2DVisual3D Rent(LooseObjectProfile profile)
    {
        if (_free.Count > 0)
        {
            Body2DVisual3D reused = _free[^1];
            _free.RemoveAt(_free.Count - 1);
            return reused;
        }

        var slot = new Body2DVisual3D { Name = $"LooseObjectVisualSlot_{_slots.Count + 1}" };
        AddChild(slot);
        // The slot needs geometry before SetVisual replaces it; this radius is the
        // placeholder only, and the real mesh is built per profile below.
        slot.Initialize(profile.Radius, profile.FillColor, profile.VisualDepthOffset);
        return slot;
    }

    /// <summary>
    /// The shared mesh for one kind at one radius. Keyed on the rounded radius because two
    /// balls of the same authored profile are the same ball, and a per-body mesh would rebuild
    /// geometry every time one spawned.
    /// </summary>
    private ArrayMesh MeshFor(LooseObjectProfile profile)
    {
        var key = (profile.Visual3D, Mathf.RoundToInt(profile.Radius * 4.0f));
        if (_meshes.TryGetValue(key, out ArrayMesh? cached))
            return cached;

        ArrayMesh built = profile.Visual3D switch
        {
            LooseObjectVisualKind.SoccerBall => LooseObjectMeshBuilder.SoccerBall(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            LooseObjectVisualKind.Can => LooseObjectMeshBuilder.Can(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            LooseObjectVisualKind.RepairKit => LooseObjectMeshBuilder.RepairKit(
                profile.Radius, profile.FillColor, profile.OutlineColor),
            _ => throw new InvalidOperationException(
                $"No loose-object mesh for {profile.Visual3D}."),
        };

        _meshes[key] = built;
        return built;
    }
}

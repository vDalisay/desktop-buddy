using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Objects;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

public partial class GrenadeVisual3D
{
    private readonly Dictionary<int, Body2DVisual3D> _multiGrenadeSlots = new();
    private readonly Dictionary<int, bool> _multiGrenadePinOut = new();
    private readonly List<Body2DVisual3D> _multiGrenadeFree = new();
    private readonly List<int> _multiGrenadeStale = new();
    private GrenadeComponent? _multiGrenadeComponent;

    public int AdditionalGrenadeVisualCount => _multiGrenadeSlots.Count;

    public override void _Ready()
    {
        // The historical presenter remains the primary/backward-compatible slot. This tiny child
        // reconciles every additional registry grenade against pooled instances using the same
        // pattern as LooseObjectVisual3D, without giving presentation its own object lifetime.
        AddChild(new GrenadeVisual3DMultiDriver(this)
        {
            Name = "GrenadeMultiVisualDriver",
            ProcessMode = ProcessModeEnum.Always,
        });
    }

    private void ReconcileAdditionalGrenades()
    {
        if (!IsInitialized)
            return;

        ResolveMultiGrenadeComponent();
        EnsureCaptureBurstSubscription();
        if (!GodotObject.IsInstanceValid(_multiGrenadeComponent) ||
            !_multiGrenadeComponent!.IsInitialized ||
            !GodotObject.IsInstanceValid(_multiGrenadeComponent.Registry))
        {
            ReleaseAllAdditionalGrenades();
            return;
        }

        LooseObjectRegistry registry = _multiGrenadeComponent.Registry;
        LooseObjectBody? primary = _slot.Target as LooseObjectBody;

        // Keep the legacy primary slot honest even when an older grenade (not the newest adopted
        // one) is the grenade whose pin was pulled. Per-runtime state, not event order, decides the
        // mesh shown on screen. Fuse punctuation is scale-only so photosensitivity-safe mode never
        // has to police a blinking emissive material.
        if (GodotObject.IsInstanceValid(primary) &&
            _multiGrenadeComponent.TryGetPresentationState(primary!.RuntimeId, out GrenadePresentationState primaryState))
        {
            bool wantsPinned = !primaryState.PinIsOut;
            if (_showingPinnedMesh != wantsPinned)
                EnsureMesh(primary.Radius, wantsPinned);
            ApplyFusePulse(_slot, primaryState);
            ApplyHeatTint(_slot, _multiGrenadeComponent.HeatOf(primary.RuntimeId));
        }
        else
        {
            _slot.Scale = Vector3.One;
        }

        _multiGrenadeStale.Clear();
        foreach ((int runtimeId, Body2DVisual3D slot) in _multiGrenadeSlots)
        {
            LooseObjectBody? body = registry.FindBody(runtimeId);
            if (!GodotObject.IsInstanceValid(body) || body == primary || !slot.IsAttached ||
                body!.SemanticContentId != ContentIds.ToolGrenade)
            {
                _multiGrenadeStale.Add(runtimeId);
            }
        }
        foreach (int runtimeId in _multiGrenadeStale)
            ReleaseAdditionalGrenade(runtimeId, registry);

        for (int index = 0; index < LooseObjectRegistry.Capacity; index++)
        {
            LooseObjectBody? body = registry.BodyAt(index);
            if (!GodotObject.IsInstanceValid(body) || body == primary ||
                body!.SemanticContentId != ContentIds.ToolGrenade)
            {
                continue;
            }

            bool hasState = _multiGrenadeComponent.TryGetPresentationState(
                body.RuntimeId, out GrenadePresentationState state);
            bool pinOut = hasState && state.PinIsOut;

            if (!_multiGrenadeSlots.TryGetValue(body.RuntimeId, out Body2DVisual3D? slot))
            {
                slot = RentAdditionalGrenade(body);
                slot.SetVisual(MeshForAdditionalGrenade(body.Radius, pinOut), _bodyMaterial, _profile.VisualDepthOffset);
                slot.PositionOffset2D = Vector2.Up * GrenadeMeshBuilder.VisualGroundOffset(_profile, body.Radius);
                slot.Attach(body);
                _multiGrenadeSlots[body.RuntimeId] = slot;
                _multiGrenadePinOut[body.RuntimeId] = pinOut;
            }
            else if (!_multiGrenadePinOut.TryGetValue(body.RuntimeId, out bool shownPinOut) || shownPinOut != pinOut)
            {
                slot.SetVisual(MeshForAdditionalGrenade(body.Radius, pinOut), _bodyMaterial, _profile.VisualDepthOffset);
                _multiGrenadePinOut[body.RuntimeId] = pinOut;
            }

            if (hasState)
                ApplyFusePulse(slot, state);
            else
                slot.Scale = Vector3.One;
            ApplyHeatTint(slot, _multiGrenadeComponent.HeatOf(body.RuntimeId));
            slot.SetPresentationActive(_presentationActive);
        }
    }

    private void CaptureAdditionalGrenadeSnapshots()
    {
        foreach (Body2DVisual3D slot in _multiGrenadeSlots.Values)
            if (GodotObject.IsInstanceValid(slot) && slot.IsAttached)
                slot.CaptureTickSnapshot();
        AdvanceCaptureBursts();
    }

    private void ResolveMultiGrenadeComponent()
    {
        if (GodotObject.IsInstanceValid(_multiGrenadeComponent))
            return;

        Node? parent = GetParent();
        if (parent is not null)
        {
            foreach (Node child in parent.GetChildren())
            {
                if (child is GrenadeComponent component)
                {
                    _multiGrenadeComponent = component;
                    return;
                }
            }
        }

        _multiGrenadeComponent = GetTree().Root.FindChild(
            nameof(GrenadeComponent), recursive: true, owned: false) as GrenadeComponent;
    }

    private Body2DVisual3D RentAdditionalGrenade(LooseObjectBody body)
    {
        if (_multiGrenadeFree.Count > 0)
        {
            Body2DVisual3D reused = _multiGrenadeFree[^1];
            _multiGrenadeFree.RemoveAt(_multiGrenadeFree.Count - 1);
            return reused;
        }

        var slot = new Body2DVisual3D
        {
            Name = $"AdditionalGrenadeVisual_{_multiGrenadeSlots.Count + 1}",
        };
        AddChild(slot);
        slot.Initialize(body.Radius, _profile.BodyColor, _profile.VisualDepthOffset);
        return slot;
    }

    private Mesh MeshForAdditionalGrenade(float radius, bool pinOut)
    {
        if (!Mathf.IsEqualApprox(radius, _builtForRadius) || _pinnedMesh is null || _pinPulledMesh is null)
        {
            _pinnedMesh = GrenadeMeshBuilder.Build(_profile, radius, pinIn: true);
            _pinPulledMesh = GrenadeMeshBuilder.Build(_profile, radius, pinIn: false);
            _builtForRadius = radius;
        }

        return pinOut ? _pinPulledMesh! : _pinnedMesh!;
    }

    /// <summary>
    /// A grenade cooking in the sprayer's flame glows towards red over the three seconds it
    /// takes to go off (owner instruction 2026-08-21), and cools back down when the flame
    /// comes off — so the tint is the readout for how close the player is.
    ///
    /// <para>Per-slot, so one grenade in the fire never reddens the rest: the first tint
    /// swaps the shared body material for this slot's own copy. A mesh swap (the pin coming
    /// out) puts the shared material back, and the next tick simply copies it again.</para>
    /// </summary>
    private void ApplyHeatTint(Body2DVisual3D slot, float heat)
    {
        if (!GodotObject.IsInstanceValid(slot) || slot.Mesh is not MeshInstance3D mesh)
            return;

        if (mesh.MaterialOverride is not StandardMaterial3D material)
            return;

        if (heat <= 0.001f && ReferenceEquals(material, _bodyMaterial))
            return;

        if (ReferenceEquals(material, _bodyMaterial))
        {
            material = (StandardMaterial3D)_bodyMaterial.Duplicate();
            material.ResourceName = "GrenadeHeatMaterial";
            mesh.MaterialOverride = material;
        }

        var glow = new Color(1.0f, 0.22f, 0.10f);
        material.AlbedoColor = Colors.White.Lerp(glow, heat);
        material.EmissionEnabled = heat > 0.02f;
        material.Emission = glow;
        material.EmissionEnergyMultiplier = heat * 1.8f;
    }

    private static void ApplyFusePulse(Body2DVisual3D slot, GrenadePresentationState state)
    {
        if (state.Stage != GrenadeFuseStage.Live || state.FuseTicksRemaining <= 0)
        {
            slot.Scale = Vector3.One;
            return;
        }

        int remaining = state.FuseTicksRemaining;
        int interval = remaining switch
        {
            <= 60 => 8,
            <= 120 => 12,
            <= 240 => 24,
            _ => 40,
        };
        float phase = (remaining % interval) / (float)interval;
        float wave = 0.5f + (0.5f * Mathf.Cos(phase * Mathf.Tau));
        float scale = 1.0f + (0.055f * wave);
        slot.Scale = new Vector3(scale, scale, scale);
    }

    private void ReleaseAdditionalGrenade(int runtimeId, LooseObjectRegistry registry)
    {
        if (!_multiGrenadeSlots.Remove(runtimeId, out Body2DVisual3D? slot))
            return;

        _multiGrenadePinOut.Remove(runtimeId);
        LooseObjectBody? body = registry.FindBody(runtimeId);
        if (GodotObject.IsInstanceValid(body))
            slot.Detach(body!);
        else
            slot.DetachAny();
        slot.Scale = Vector3.One;
        slot.SetPresentationActive(false);
        _multiGrenadeFree.Add(slot);
    }

    private void ReleaseAllAdditionalGrenades()
    {
        _multiGrenadeStale.Clear();
        foreach (int runtimeId in _multiGrenadeSlots.Keys)
            _multiGrenadeStale.Add(runtimeId);
        foreach (int runtimeId in _multiGrenadeStale)
        {
            if (_multiGrenadeSlots.Remove(runtimeId, out Body2DVisual3D? slot))
            {
                slot.DetachAny();
                slot.Scale = Vector3.One;
                slot.SetPresentationActive(false);
                _multiGrenadeFree.Add(slot);
            }
            _multiGrenadePinOut.Remove(runtimeId);
        }
    }

    private sealed partial class GrenadeVisual3DMultiDriver : Node
    {
        private readonly GrenadeVisual3D _owner;

        public GrenadeVisual3DMultiDriver(GrenadeVisual3D owner) => _owner = owner;

        public override void _Process(double delta) => _owner.ReconcileAdditionalGrenades();

        public override void _PhysicsProcess(double delta) => _owner.CaptureAdditionalGrenadeSnapshots();

        public override void _ExitTree() => _owner.ReleaseCaptureBurstSubscription();
    }
}

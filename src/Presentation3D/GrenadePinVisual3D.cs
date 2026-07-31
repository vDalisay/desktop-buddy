using System;
using System.Collections.Generic;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The dropped pins, in the frontal 3D presentation. <see cref="PinBody"/> is a cosmetic
/// body that draws its own flat ring; this node draws the same pin as a lit mesh and turns
/// that flat drawing off, so the pin obeys the same rule as the grenade and the guns — one
/// silhouette per presentation mode, never both at once.
///
/// <para>Render-only, and pooled exactly like the bodies it follows: one mesh instance per
/// pooled pin, built once, shown only while its pin is live. It never drops, parks, or moves
/// a pin — that is <see cref="GrenadeComponent"/>'s, on the routed tick.</para>
///
/// <para>Motion is interpolated from a snapshot the owning presenter captures before each
/// solver step, the same way <see cref="Body2DVisual3D"/> does it. A pin is small and fast
/// enough that the stepped alternative reads as a stutter.</para>
/// </summary>
[GlobalClass]
public partial class GrenadePinVisual3D : Node3D
{
    private readonly List<PinBody> _pins = new();
    private readonly List<MeshInstance3D> _meshes = new();
    private readonly List<StandardMaterial3D> _materials = new();
    private readonly List<Vector2> _previousPositions = new();
    private readonly List<float> _previousRotations = new();

    private GrenadeProfile _profile = null!;
    private float _depthOffset;
    private bool _presentationActive;

    public bool IsInitialized { get; private set; }

    /// <summary>How many pin meshes are currently drawn — the scenario-visible count.</summary>
    public int VisiblePinCount { get; private set; }

    public void Initialize(GrenadeProfile profile)
    {
        if (IsInitialized)
            return;

        ArgumentNullException.ThrowIfNull(profile);
        if (!GodotObject.IsInstanceValid(profile))
            throw new ArgumentException("The pin visual requires a live profile.", nameof(profile));

        _profile = profile;
        _depthOffset = profile.VisualDepthOffset;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        Visible = false;
        IsInitialized = true;
    }

    /// <summary>
    /// Adopts the component's pooled pins. Called once on composition — the pool is fixed
    /// for the run, so there is no per-drop allocation and no per-tick search.
    /// </summary>
    public void TrackPins(IReadOnlyList<PinBody> pins)
    {
        RequireInitialized();
        ArgumentNullException.ThrowIfNull(pins);
        if (_pins.Count > 0)
            return;

        Mesh mesh = GrenadeMeshBuilder.BuildPin(_profile, PinBody.RingRadiusPx);
        foreach (PinBody pin in pins)
        {
            if (!GodotObject.IsInstanceValid(pin))
                continue;

            // One material per pin: the linger fade is per-pin alpha, so they cannot share.
            var material = new StandardMaterial3D
            {
                ResourceName = "ProvisionalGrenadePinMaterial",
                AlbedoColor = Colors.White,
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.45f,
                Metallic = 0.35f,
            };
            var instance = new MeshInstance3D
            {
                Name = $"{pin.Name}Mesh",
                Mesh = mesh,
                MaterialOverride = material,
                Visible = false,
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            };
            AddChild(instance);

            _pins.Add(pin);
            _meshes.Add(instance);
            _materials.Add(material);
            _previousPositions.Add(pin.GlobalPosition);
            _previousRotations.Add(pin.Rotation);
        }

        ApplyLegacyDrawing();
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        Visible = active;
        ApplyLegacyDrawing();
    }

    /// <summary>Captures the end of the previous solver step, for interpolation.</summary>
    public void CaptureTickSnapshot()
    {
        for (int index = 0; index < _pins.Count; index++)
        {
            PinBody pin = _pins[index];
            if (!GodotObject.IsInstanceValid(pin))
                continue;

            _previousPositions[index] = pin.GlobalPosition;
            _previousRotations[index] = pin.Rotation;
        }
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized || !_presentationActive)
            return;

        float fraction = Mathf.Clamp(
            (float)Engine.GetPhysicsInterpolationFraction(), 0.0f, 1.0f);
        int visible = 0;
        for (int index = 0; index < _pins.Count; index++)
        {
            PinBody pin = _pins[index];
            MeshInstance3D instance = _meshes[index];
            if (!GodotObject.IsInstanceValid(pin) || !pin.IsLive)
            {
                instance.Visible = false;
                continue;
            }

            Vector2 position2D = _previousPositions[index].Lerp(pin.GlobalPosition, fraction);
            Vector3 position = WorldPlaneMapping.To3D(position2D);
            position.Z = _depthOffset;
            instance.GlobalPosition = position;
            instance.GlobalRotation = new Vector3(
                0.0f,
                0.0f,
                WorldPlaneMapping.To3DRotationZ(
                    Mathf.LerpAngle(_previousRotations[index], pin.Rotation, fraction)));

            // The mesh already carries the pin colour per vertex, so albedo stays white
            // and only its alpha moves — otherwise the linger fade would also darken it.
            _materials[index].AlbedoColor = new Color(
                1.0f, 1.0f, 1.0f, Mathf.Clamp(pin.FadeAlpha, 0.0f, 1.0f));
            instance.Visible = true;
            visible++;
        }

        VisiblePinCount = visible;
    }

    /// <summary>
    /// Hands the flat drawing back and forth. While this presenter is active the bodies
    /// stop drawing themselves; in legacy circles they take it back.
    /// </summary>
    private void ApplyLegacyDrawing()
    {
        foreach (PinBody pin in _pins)
        {
            if (GodotObject.IsInstanceValid(pin))
                pin.SetLegacyDrawEnabled(!_presentationActive);
        }
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("GrenadePinVisual3D used before initialization.");
    }
}

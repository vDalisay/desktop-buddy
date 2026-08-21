using System;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Presentation3D;

/// <summary>
/// The 3D Brush and feather duster held at the pointer. Unlike the other cursor tools these have
/// no physics body — care contact is CareStrokeComponent's cursor/part geometry — so this places
/// itself straight from the routed pointer instead of tracking a RigidBody2D. Presentation only.
/// </summary>
[GlobalClass]
public partial class CareToolVisual3D : Node3D
{
    /// <summary>
    /// The depth every cursor tool renders at. The buddy's own parts run from -48 (feet) to 96
    /// (head), so anything shallower clips through the torso and hides behind the head — which is
    /// exactly what the brush did at 24 (owner report 2026-08-19).
    /// </summary>
    private const float DepthOffset = 144.0f;

    private CareStrokeComponent _careStroke = null!;
    private MeshInstance3D _feather = null!;
    private MeshInstance3D _brush = null!;
    private CareToolSway _sway;
    private bool _presentationActive;
    private bool _reducedParticles;
    private ToolId _tool;
    private bool _held;
    private Vector2 _pointer;
    private double _phase;

    public bool IsInitialized { get; private set; }
    public bool IsFeatherVisible => IsInitialized && _feather.Visible;
    public bool IsBrushVisible => IsInitialized && _brush.Visible;

    public void Initialize(CareStrokeComponent careStroke)
    {
        if (IsInitialized)
            return;

        if (!GodotObject.IsInstanceValid(careStroke) || !careStroke.IsInitialized)
            throw new InvalidOperationException(
                "CareToolVisual3D requires an initialized CareStrokeComponent.");

        _careStroke = careStroke;

        _feather = BuildInstance("FeatherDusterVisual", CareToolMeshBuilder.BuildFeatherDuster());
        _brush = BuildInstance("BrushVisual", CareToolMeshBuilder.BuildBrush());
        BuildFluff();
        IsInitialized = true;
        Visible = false;
    }

    public void SetPresentationActive(bool active)
    {
        _presentationActive = active;
        if (!active)
        {
            _sway.Reset();
            ClearFluff();
        }
        ApplyVisibility();
    }

    /// <summary>Mirrors <see cref="Buddy.Presentation.ToolCursorPresenter.SetPointerState"/>.</summary>
    public void SetPointerState(ToolId tool, Vector2 worldPosition, bool held)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("CareToolVisual3D used before initialization.");

        _tool = tool;
        _held = held;
        _pointer = worldPosition;
        ApplyVisibility();
        ApplyTransforms();
    }

    public override void _Process(double delta)
    {
        if (!IsInitialized)
            return;

        // The barbs outlive the feather being put away, so they are ticked before the early
        // return that parks everything else.
        TickFluff(delta);
        if (!Visible)
        {
            _sway.Reset();
            return;
        }

        _phase += delta;
        _sway.Tick(_pointer, _careStroke.FeatherAngle, _careStroke.IsWiggling, delta);
        ApplyTransforms();
    }

    /// <summary>Honours the Reduced Particles effects setting for the tickle barbs.</summary>
    public void ApplyEffectsSettings(bool reducedParticles)
    {
        _reducedParticles = reducedParticles;
        if (reducedParticles)
            ClearFluff();
    }

    private void ApplyVisibility()
    {
        if (!IsInitialized)
            return;

        bool show = _presentationActive && _held;
        _feather.Visible = show && _tool == ToolId.Tickle;
        _brush.Visible = show && _tool == ToolId.Pet;
        Visible = _feather.Visible || _brush.Visible || ActiveFluffCount > 0;
    }

    private void ApplyTransforms()
    {
        if (_feather.Visible)
        {
            // A slow idle breath under the sway, so a parked feather is not dead still.
            float breath = Mathf.Sin((float)_phase * 3.2f) * 0.035f;
            // The stick points where the aim points; the sway is a wobble on top of it.
            Place(
                _feather,
                _pointer + CareToolGeometry.GripOffset,
                _careStroke.FeatherAngle + _sway.Angle + breath);
        }

        if (_brush.Visible)
        {
            // The same rub the legacy drawing has, so switching presentation modes does not
            // change how brushing reads.
            float rub = Mathf.Sin((float)_phase * 18.0f) * 3.0f;
            Place(_brush, _pointer + new Vector2(10.0f + rub, 12.0f), -0.35f);
        }
    }

    private static void Place(MeshInstance3D instance, Vector2 world, float rotation2D)
    {
        Vector3 position = WorldPlaneMapping.To3D(world);
        position.Z = DepthOffset;
        instance.Position = position;
        instance.Rotation = new Vector3(0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(rotation2D));
    }

    private MeshInstance3D BuildInstance(string name, ArrayMesh mesh)
    {
        var instance = new MeshInstance3D
        {
            Name = name,
            Mesh = mesh,
            MaterialOverride = new StandardMaterial3D
            {
                ResourceName = name + "Material",
                AlbedoColor = Colors.White,
                VertexColorUseAsAlbedo = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.PerPixel,
                Roughness = 0.7f,
                Metallic = 0.0f,
            },
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(instance);
        return instance;
    }
}

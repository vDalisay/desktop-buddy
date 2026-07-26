using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using Godot;
using System;

namespace DesktopBuddy.Tools;

/// <summary>
/// The Boxing Glove's physical collider. It lives on the PhysicalTools layer so
/// it strikes buddy parts, loose objects, and room bounds but never projectiles
/// or other tools. Contacts attribute to the Boxing Glove tool for statistics
/// and harmful-history memory (RAGDOLL §7.1); pain comes only from the measured
/// impulse through the shared curve.
/// </summary>
[GlobalClass]
public partial class BoxingGloveBody : RigidBody2D, IImpactSource, IBody2DVisualPulseSource
{
    private const int CircleSegments = 32;
    private const float OutlineWidth = 2.0f;
    private static readonly Color OutlineColor = new("5c1a1a");
    private Color _gloveColor = new("e05b4b");

    public float Radius { get; private set; } = 14.0f;
    private ulong _pulseStartedUsec;
    private double _pulseSeconds;
    private float _pulseIntensity;
    private float _pulseAngle;

    public int InteractionId { get; } = InteractionIds.Next();

    public string ContentId => ContentIds.ToolBoxingGlove;
    public bool IsImpactPulsing { get; private set; }
    public bool IsImpactArmed { get; private set; }
    public Vector2 VisualScale2D
    {
        get
        {
            float pulse = CurrentPulseStrength();
            return new Vector2(1.0f - pulse * 0.24f, 1.0f + pulse * 0.18f);
        }
    }
    public float VisualRotation2D => IsImpactPulsing ? _pulseAngle : 0.0f;

    public void Configure(BoxingGloveProfile profile)
    {
        Radius = profile.Radius;
        _gloveColor = profile.VisualColor;
        Mass = profile.Mass;
        LinearDamp = profile.LinearDamp;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = profile.Radius } });
        CollisionLayer = CollisionLayers.PhysicalTools;
        // Selection alone must never create an overlap impulse or payout. The
        // controller enables the normal mask only after real pointer travel.
        CollisionMask = 0;
        // The tether must never fight the sleep heuristic while the tool is held.
        CanSleep = false;
        QueueRedraw();
    }

    public void ArmImpacts()
    {
        if (IsImpactArmed)
            return;
        IsImpactArmed = true;
        CollisionMask = CollisionLayers.MaskPhysicalTools;
    }

    public override void _Draw()
    {
        if (IsImpactPulsing)
        {
            float pulse = CurrentPulseStrength();
            DrawSetTransform(
                Vector2.Zero,
                _pulseAngle,
                new Vector2(1.0f - pulse * 0.24f, 1.0f + pulse * 0.18f));
        }
        DrawCircle(Vector2.Zero, Radius, _gloveColor, true, -1.0f, true);
        DrawArc(Vector2.Zero, Radius, 0.0f, Mathf.Tau, CircleSegments, OutlineColor, OutlineWidth, true);
    }

    public override void _Process(double delta)
    {
        if (!IsImpactPulsing)
            return;
        double elapsed = (Time.GetTicksUsec() - _pulseStartedUsec) / 1_000_000.0;
        if (elapsed >= _pulseSeconds)
            IsImpactPulsing = false;
        QueueRedraw();
    }

    private float CurrentPulseStrength()
    {
        if (!IsImpactPulsing)
        {
            return 0.0f;
        }

        double elapsed = (Time.GetTicksUsec() - _pulseStartedUsec) / 1_000_000.0;
        float decay = Mathf.Clamp(1.0f - (float)(elapsed / _pulseSeconds), 0.0f, 1.0f);
        return _pulseIntensity * decay;
    }

    public void PulseImpact(Vector2 normal, float intensity, double seconds)
    {
        _pulseStartedUsec = Time.GetTicksUsec();
        _pulseSeconds = Math.Max(0.001, seconds);
        _pulseIntensity = Mathf.Clamp(intensity, 0.0f, 1.0f);
        _pulseAngle = normal.IsZeroApprox() ? 0.0f : normal.Angle();
        IsImpactPulsing = true;
        QueueRedraw();
    }
}

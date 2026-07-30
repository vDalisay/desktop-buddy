using DesktopBuddy.Domain.Content;
using DesktopBuddy.Interaction;
using DesktopBuddy.App;
using DesktopBuddy.Presentation3D;
using Godot;
using System;

namespace DesktopBuddy.Tools;

/// <summary>
/// The physical collider of a cursor-tethered tool. It lives on the PhysicalTools
/// layer so it strikes buddy parts, loose objects, and room bounds but never
/// projectiles or other tools. Contacts attribute to the authored content ID for
/// statistics and harmful-history memory (RAGDOLL §7.1); pain comes only from the
/// measured impulse through the shared curve, so a heavier or longer tool hurts
/// more only because it really does hit harder.
/// </summary>
[GlobalClass]
public partial class CursorToolBody : RigidBody2D, IImpactSource, ISwingImpactSource, IBody2DVisualPulseSource
{
    private const int CircleSegments = 32;
    private const float OutlineWidth = 2.0f;

    private Color _fillColor = new("e05b4b");
    private Color _outlineColor = new("5c1a1a");
    private string _contentId = ContentIds.ToolBoxingGlove;
    private ulong _pulseStartedUsec;
    private double _pulseSeconds;
    private float _pulseIntensity;
    private float _pulseAngle;

    public float Radius { get; private set; } = 14.0f;

    /// <summary>Total length along the local Y axis, or <c>0</c> for a circle.</summary>
    public float Length { get; private set; }

    public bool IsElongated => Length > 0.0f;

    public int InteractionId { get; } = InteractionIds.Next();

    public string ContentId => _contentId;
    public bool IsImpactPulsing { get; private set; }
    public bool IsImpactArmed { get; private set; }

    /// <summary>
    /// What the player was doing with this tool when the solver produced the
    /// contacts now being observed. The controller pushes this every routed
    /// tick, including the brief grace after a swing ends, so the pipeline never
    /// has to ask a mutable controller "what charge are you at?" a tick late and
    /// get an answer about the wrong moment.
    /// </summary>
    public SwingImpactContext SwingContext { get; private set; } = SwingImpactContext.FreeSwing;

    /// <summary>Called only from the owning controller's routed fixed tick.</summary>
    public void SetSwingContext(SwingImpactContext context) => SwingContext = context;

    public Vector2 VisualScale2D
    {
        get
        {
            float pulse = CurrentPulseStrength();
            return new Vector2(1.0f - pulse * 0.24f, 1.0f + pulse * 0.18f);
        }
    }

    /// <summary>
    /// The squash of a round tool is oriented by the impact normal, which reads as
    /// the glove deforming against what it hit. An elongated tool already carries a
    /// meaningful rotation of its own — the swing — so tilting it by the contact
    /// normal would detach the drawing from the collider; it squashes in place.
    /// </summary>
    public float VisualRotation2D => IsImpactPulsing && !IsElongated ? _pulseAngle : 0.0f;

    public void Configure(CursorToolProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        Radius = profile.Radius;
        Length = profile.Length;
        _fillColor = profile.VisualColor;
        _outlineColor = profile.OutlineColor;
        _contentId = profile.ContentId;
        Mass = profile.Mass;
        LinearDamp = profile.LinearDamp;
        AngularDamp = profile.AngularDamp;
        AddChild(new CollisionShape2D { Shape = BuildShape(profile) });
        CollisionLayer = CollisionLayers.PhysicalTools;
        // Selection alone must never create an overlap impulse or payout. The
        // controller enables the normal mask only after real pointer travel.
        CollisionMask = 0;
        // The tether must never fight the sleep heuristic while the tool is held.
        CanSleep = false;
        QueueRedraw();
    }

    private static Shape2D BuildShape(CursorToolProfile profile) =>
        profile.IsElongated
            ? new CapsuleShape2D { Radius = profile.Radius, Height = profile.Length }
            : new CircleShape2D { Radius = profile.Radius };

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
                VisualRotation2D,
                new Vector2(1.0f - pulse * 0.24f, 1.0f + pulse * 0.18f));
        }

        if (!IsElongated)
        {
            DrawCircle(Vector2.Zero, Radius, _fillColor, true, -1.0f, true);
            DrawArc(Vector2.Zero, Radius, 0.0f, Mathf.Tau, CircleSegments, _outlineColor, OutlineWidth, true);
            return;
        }

        // A capsule drawn as its own parts: the shaft plus the two rounded ends,
        // matching CapsuleShape2D's long-axis-is-local-Y convention exactly.
        float halfShaft = (Length * 0.5f) - Radius;
        var shaft = new Rect2(-Radius, -halfShaft, Radius * 2.0f, halfShaft * 2.0f);
        var top = new Vector2(0.0f, -halfShaft);
        var bottom = new Vector2(0.0f, halfShaft);
        DrawRect(shaft, _fillColor, true);
        DrawCircle(top, Radius, _fillColor, true, -1.0f, true);
        DrawCircle(bottom, Radius, _fillColor, true, -1.0f, true);
        DrawArc(top, Radius, Mathf.Pi, Mathf.Tau, CircleSegments, _outlineColor, OutlineWidth, true);
        DrawArc(bottom, Radius, 0.0f, Mathf.Pi, CircleSegments, _outlineColor, OutlineWidth, true);
        DrawLine(new Vector2(-Radius, -halfShaft), new Vector2(-Radius, halfShaft), _outlineColor, OutlineWidth, true);
        DrawLine(new Vector2(Radius, -halfShaft), new Vector2(Radius, halfShaft), _outlineColor, OutlineWidth, true);
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

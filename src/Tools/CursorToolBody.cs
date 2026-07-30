using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
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
    private ulong _chargeVisualStartedUsec;
    private float _chargeShakeAmplitude;
    private float _chargeShakePrimaryHz;
    private float _chargeShakeSecondaryHz;
    private ulong _glintStartedUsec;
    private double _glintSeconds;
    private float _glintSizePx;
    private bool _glintActive;

    public float Radius { get; private set; } = 14.0f;

    /// <summary>Total length along the local Y axis, or <c>0</c> for a circle.</summary>
    public float Length { get; private set; }

    public bool IsElongated => Length > 0.0f;

    public int InteractionId { get; } = InteractionIds.Next();

    public string ContentId => _contentId;
    public bool IsImpactPulsing { get; private set; }
    public bool IsImpactArmed { get; private set; }
    public int ChargeGlintStarts { get; private set; }
    public float ChargeShakeAmplitude => _chargeShakeAmplitude;
    public bool IsChargeGlintActive => _glintActive;

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

    /// <summary>
    /// The charge wobble is computed from monotonic presentation time and never
    /// written into the RigidBody2D transform. That keeps the collider perfectly
    /// still while the bat visibly strains at full charge.
    /// </summary>
    public Vector2 VisualOffset2D
    {
        get
        {
            if (_chargeShakeAmplitude <= 0.0f)
            {
                return Vector2.Zero;
            }

            float elapsed = (Time.GetTicksUsec() - _chargeVisualStartedUsec) / 1_000_000.0f;
            System.Numerics.Vector2 offset = ChargedSwing.ShakeOffset(
                elapsed,
                _chargeShakeAmplitude,
                _chargeShakePrimaryHz,
                _chargeShakeSecondaryHz);
            return new Vector2(offset.X, offset.Y);
        }
    }

    public float VisualGlintStrength => CurrentGlintStrength();
    public float VisualGlintSizePx => _glintSizePx;
    public Vector2 VisualGlintLocalPosition =>
        IsElongated ? new Vector2(0.0f, Length * -0.5f) : Vector2.Zero;

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
        float pulse = CurrentPulseStrength();
        DrawSetTransform(
            VisualOffset2D,
            VisualRotation2D,
            new Vector2(1.0f - pulse * 0.24f, 1.0f + pulse * 0.18f));

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

        DrawChargeGlint();
    }

    public override void _Process(double delta)
    {
        if (IsImpactPulsing)
        {
            double elapsed = (Time.GetTicksUsec() - _pulseStartedUsec) / 1_000_000.0;
            if (elapsed >= _pulseSeconds)
                IsImpactPulsing = false;
        }

        if (_glintActive)
        {
            double elapsed = (Time.GetTicksUsec() - _glintStartedUsec) / 1_000_000.0;
            if (elapsed >= _glintSeconds)
            {
                _glintActive = false;
            }

            // The expiry redraw is essential for the legacy CanvasItem path:
            // without it, the last star draw command would remain cached.
            QueueRedraw();
        }

        if (_chargeShakeAmplitude > 0.0f || IsImpactPulsing)
        {
            QueueRedraw();
        }
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

    /// <summary>
    /// Update the render-only charge wobble from the routed gameplay charge.
    /// The amplitude is the domain model's eased value; presentation time only
    /// chooses where inside the deterministic two-frequency wobble it is drawn.
    /// </summary>
    public void SetChargeVisual(float charge, SwingToolProfile? profile)
    {
        if (profile is null || charge <= 0.0f)
        {
            if (_chargeShakeAmplitude <= 0.0f)
            {
                return;
            }

            _chargeShakeAmplitude = 0.0f;
            _chargeShakePrimaryHz = 0.0f;
            _chargeShakeSecondaryHz = 0.0f;
            QueueRedraw();
            return;
        }

        if (_chargeShakeAmplitude <= 0.0f)
        {
            _chargeVisualStartedUsec = Time.GetTicksUsec();
        }

        _chargeShakeAmplitude = ChargedSwing.ShakeAmplitude(
            charge, profile.ShakeMaxAmplitudePx);
        _chargeShakePrimaryHz = profile.ShakePrimaryHz;
        _chargeShakeSecondaryHz = profile.ShakeSecondaryHz;
    }

    /// <summary>Start the one-shot full-charge star at the barrel tip.</summary>
    public void StartChargeGlint(double seconds, float sizePx)
    {
        _glintStartedUsec = Time.GetTicksUsec();
        _glintSeconds = Math.Max(0.001, seconds);
        _glintSizePx = Math.Max(1.0f, sizePx);
        _glintActive = true;
        ChargeGlintStarts++;
        QueueRedraw();
    }

    private float CurrentGlintStrength()
    {
        if (!_glintActive || _glintSeconds <= 0.0)
        {
            return 0.0f;
        }

        double elapsed = (Time.GetTicksUsec() - _glintStartedUsec) / 1_000_000.0;
        if (elapsed >= _glintSeconds)
        {
            return 0.0f;
        }

        // Fast scale-pop followed by a longer ease-out tail.
        float progress = Mathf.Clamp((float)(elapsed / _glintSeconds), 0.0f, 1.0f);
        return progress < 0.25f
            ? progress / 0.25f
            : 1.0f - ((progress - 0.25f) / 0.75f);
    }

    private void DrawChargeGlint()
    {
        float strength = CurrentGlintStrength();
        if (strength <= 0.0f || !IsElongated)
        {
            return;
        }

        Vector2 tip = VisualGlintLocalPosition;
        float radius = _glintSizePx * 0.5f * strength;
        Color glow = new Color(1.0f, 0.94f, 0.58f, 0.9f * strength);
        DrawLine(tip + Vector2.Left * radius, tip + Vector2.Right * radius, glow, 2.0f, true);
        DrawLine(tip + Vector2.Up * radius, tip + Vector2.Down * radius, glow, 2.0f, true);
        float diagonal = radius * 0.62f;
        DrawLine(
            tip + new Vector2(-diagonal, -diagonal),
            tip + new Vector2(diagonal, diagonal),
            glow,
            1.5f,
            true);
        DrawLine(
            tip + new Vector2(diagonal, -diagonal),
            tip + new Vector2(-diagonal, diagonal),
            glow,
            1.5f,
            true);
    }
}

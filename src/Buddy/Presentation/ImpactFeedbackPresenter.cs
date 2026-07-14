using System;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>
/// Presentation response for Boxing Glove impacts. It draws a short world ring
/// and canvas-edge jolt without moving the OS window or gameplay nodes. Only a
/// maximum-pain or knockout hit starts the confirmed non-stacking hit-stop.
/// </summary>
[GlobalClass]
public partial class ImpactFeedbackPresenter : Node2D
{
    private static readonly Color RingColor = new("ffd166");
    private static readonly Color FlashColor = new(1.0f, 0.82f, 0.38f, 0.35f);

    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public BoxingGloveController Glove { get; set; } = null!;
    [Export] public ImpactFeedbackProfile Profile { get; set; } = null!;

    private ulong _feedbackStartedUsec;
    private Vector2 _impactWorldPoint;
    private Vector2 _impactNormal;
    private float _impactIntensity;
    private ulong _hitStopStartedUsec;
    private double _resumeScale = 1.0;

    public bool IsInitialized { get; private set; }
    public bool IsFeedbackActive { get; private set; }
    public bool IsHitStopActive { get; private set; }
    public int FeedbackCount { get; private set; }
    public int HitStopTriggerCount { get; private set; }
    public float LastAppliedHitStopScale { get; private set; } = 1.0f;
    public Vector2 LastImpactWorldPoint => _impactWorldPoint;
    public Vector2 LastImpactLocalPoint => ToLocal(_impactWorldPoint);

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(Glove) || !Glove.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("ImpactFeedbackPresenter dependencies are incomplete or invalid.");
        }

        ZAsRelative = false;
        ZIndex = 150;
        Pipeline.ImpactAccepted += OnImpact;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
            Pipeline.ImpactAccepted -= OnImpact;
        if (IsHitStopActive)
            Engine.TimeScale = _resumeScale;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (IsHitStopActive)
        {
            double elapsed = (now - _hitStopStartedUsec) / 1_000_000.0;
            double progress = Math.Clamp(elapsed / Profile.HitStopSeconds, 0.0, 1.0);
            LastAppliedHitStopScale = EvaluateHitStopScale(Profile.HitStopScale, progress);
            Engine.TimeScale = _resumeScale * LastAppliedHitStopScale;
            if (progress >= 1.0)
            {
                Engine.TimeScale = _resumeScale;
                LastAppliedHitStopScale = 1.0f;
                IsHitStopActive = false;
            }
        }

        if (IsFeedbackActive)
        {
            double elapsed = (now - _feedbackStartedUsec) / 1_000_000.0;
            if (elapsed >= Profile.RingSeconds)
                IsFeedbackActive = false;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!IsFeedbackActive)
            return;

        double elapsed = (Time.GetTicksUsec() - _feedbackStartedUsec) / 1_000_000.0;
        float progress = Mathf.Clamp((float)(elapsed / Profile.RingSeconds), 0.0f, 1.0f);
        float alpha = 1.0f - progress;
        float radius = Mathf.Lerp(9.0f, 38.0f, progress);
        Color ring = new(RingColor, alpha * _impactIntensity);
        Vector2 impactLocal = ToLocal(_impactWorldPoint);
        DrawArc(impactLocal, radius, 0, Mathf.Tau, 32, ring, 3.0f, true);

        Vector2 normal = _impactNormal.IsZeroApprox() ? Vector2.Up : _impactNormal.Normalized();
        for (int ray = -1; ray <= 1; ray++)
        {
            Vector2 direction = normal.Rotated(ray * 0.45f);
            DrawLine(
                impactLocal + direction * (radius * 0.35f),
                impactLocal + direction * radius,
                ring,
                2.0f,
                true);
        }

        // A presentation-only edge jolt: the world/camera and pointer mapping do
        // not move, so it cannot corrupt physics or desktop coordinates.
        Rect2 viewport = GetViewportRect();
        float jolt = Mathf.Sin(progress * Mathf.Tau * 3.0f) *
                      Profile.CanvasJoltPixels * alpha * _impactIntensity;
        Color flash = new(FlashColor, FlashColor.A * alpha * _impactIntensity);
        Vector2 joltOffset = new(jolt, 0.0f);
        Vector2 topLeft = MakeCanvasPositionLocal(viewport.Position + joltOffset);
        Vector2 topRight = MakeCanvasPositionLocal(viewport.Position + new Vector2(viewport.Size.X, 0.0f) + joltOffset);
        Vector2 bottomRight = MakeCanvasPositionLocal(viewport.End + joltOffset);
        Vector2 bottomLeft = MakeCanvasPositionLocal(viewport.Position + new Vector2(0.0f, viewport.Size.Y) + joltOffset);
        DrawPolyline(
            new[] { topLeft, topRight, bottomRight, bottomLeft, topLeft },
            flash,
            2.0f,
            true);
    }

    private void OnImpact(AcceptedImpact impact)
    {
        if (impact.ContentId != (int)ToolId.BoxingGlove)
            return;

        _feedbackStartedUsec = Time.GetTicksUsec();
        _impactWorldPoint = impact.Point;
        _impactNormal = impact.Normal;
        _impactIntensity = Mathf.Clamp(impact.Pain / Profile.MaximumPain, 0.25f, 1.0f);
        IsFeedbackActive = true;
        FeedbackCount++;
        Glove.Glove?.PulseImpact(impact.Normal, _impactIntensity, Profile.GloveSquashSeconds);
        QueueRedraw();

        if ((impact.Pain + 0.0001f >= Profile.MaximumPain || impact.KnockoutTriggered) &&
            !IsHitStopActive)
        {
            _resumeScale = Engine.TimeScale;
            _hitStopStartedUsec = Time.GetTicksUsec();
            IsHitStopActive = true;
            HitStopTriggerCount++;
            LastAppliedHitStopScale = Profile.HitStopScale;
            Engine.TimeScale = _resumeScale * Profile.HitStopScale;
        }
    }

    /// <summary>
    /// The confirmed envelope still rises continuously over 0.12 real seconds,
    /// but an ease-in cubic preserves the visibly slow early portion instead of
    /// spending most of the duration already near full speed.
    /// </summary>
    public static float EvaluateHitStopScale(float startingScale, double progress)
    {
        float clamped = Mathf.Clamp((float)progress, 0.0f, 1.0f);
        float eased = clamped * clamped * clamped;
        return Mathf.Lerp(startingScale, 1.0f, eased);
    }
}

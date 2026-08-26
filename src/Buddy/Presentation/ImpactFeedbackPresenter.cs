using DesktopBuddy.Domain.Content;
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
///
/// <para><b>The cartoon impact frame.</b> Animation sells a hit with a very short
/// held frame and a burst drawn on top of the contact point, not with a long
/// effect: the hold comes first, the motion resumes out of it, and everything the
/// eye reads as "POW" happens inside the first tenth of a second. So the layers
/// here are stacked shortest-first — a white starburst impact frame for
/// <see cref="ImpactFrameSeconds"/>, radial speed lines behind it for
/// <see cref="SpeedLineSeconds"/>, then the shockwave ring easing out over the
/// authored ring duration — and the hit-stop that already existed is what holds
/// the frame while the first of those is on screen.</para>
///
/// <para><b>Accessibility (FR-017.3).</b> Reduced Motion suppresses the hit-stop
/// entirely, Screen Shake gates the canvas-edge jolt, Reduced Particles thins the
/// speed lines through the shared stride, and Photosensitivity Safe caps the white
/// impact-frame flash to a dim warm tone instead of a full-white pop. None of this
/// reaches damage, pain, payout, or contact authority.</para>
/// </summary>
[GlobalClass]
public partial class ImpactFeedbackPresenter : Node2D
{
    /// <summary>The held white frame. Six 120 Hz ticks: long enough to see, short enough to punch.</summary>
    private const double ImpactFrameSeconds = 0.05;

    /// <summary>Radial speed lines outlive the frame slightly so the burst reads as motion.</summary>
    private const double SpeedLineSeconds = 0.10;

    private const int SpeedLineCount = 12;
    private const int StarPoints = 7;

    private static readonly Color RingColor = new("ffd166");
    private static readonly Color FlashColor = new(1.0f, 0.82f, 0.38f, 0.35f);
    private static readonly Color HomeRunBurstColor = new("fff3b0");
    private static readonly Color ImpactFrameColor = new("ffffff");
    private static readonly Color SafeImpactFrameColor = new("ffd9a0");
    private static readonly Color SpeedLineColor = new("fff0c2");

    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public CursorToolController CursorTools { get; set; } = null!;
    [Export] public SwingHitLagComponent HitLag { get; set; } = null!;
    [Export] public ImpactFeedbackProfile Profile { get; set; } = null!;

    private ulong _feedbackStartedUsec;
    private Vector2 _impactWorldPoint;
    private Vector2 _impactNormal;
    private float _impactIntensity;
    private ulong _homeRunBurstStartedUsec;
    private ulong _hitStopStartedUsec;
    private double _resumeScale = 1.0;
    private Domain.Presentation.EffectsSettings _effects =
        Domain.Presentation.EffectsSettings.Default;

    public bool IsInitialized { get; private set; }
    public bool IsFeedbackActive { get; private set; }
    public bool IsHomeRunBurstActive { get; private set; }
    public bool IsHitStopActive { get; private set; }
    public int FeedbackCount { get; private set; }
    public int HomeRunBurstCount { get; private set; }
    public int HitStopTriggerCount { get; private set; }
    public float LastAppliedHitStopScale { get; private set; } = 1.0f;
    public Vector2 LastImpactWorldPoint => _impactWorldPoint;
    public Vector2 LastImpactLocalPoint => ToLocal(_impactWorldPoint);
    public Vector2 LastHomeRunBurstWorldPoint { get; private set; }

    /// <summary>
    /// The effect settings currently in force. Presentation only — see the class remarks.
    /// </summary>
    public Domain.Presentation.EffectsSettings Effects => _effects;

    public void ApplyEffectsSettings(Domain.Presentation.EffectsSettings settings)
    {
        _effects = settings;
        if (settings.ReducedMotion)
            StopHitStop();
    }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(CursorTools) || !CursorTools.IsInitialized ||
            !GodotObject.IsInstanceValid(HitLag) || !HitLag.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) || Profile.Validate().Count > 0)
        {
            throw new InvalidOperationException("ImpactFeedbackPresenter dependencies are incomplete or invalid.");
        }

        ZAsRelative = false;
        ZIndex = 150;
        Pipeline.ImpactAccepted += OnImpact;
        HitLag.Started += OnHitLagStarted;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
            Pipeline.ImpactAccepted -= OnImpact;
        if (GodotObject.IsInstanceValid(HitLag))
            HitLag.Started -= OnHitLagStarted;
        if (IsHitStopActive)
            Engine.TimeScale = _resumeScale;
    }

    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        bool visualExpired = false;
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
            {
                IsFeedbackActive = false;
                visualExpired = true;
            }
        }

        if (IsHomeRunBurstActive)
        {
            double elapsed = (now - _homeRunBurstStartedUsec) / 1_000_000.0;
            if (elapsed >= Profile.HomeRunBurstSeconds)
            {
                IsHomeRunBurstActive = false;
                visualExpired = true;
            }
        }

        // CanvasItem drawing is retained until the next redraw. Queue one final frame when an
        // effect expires so WebGL does not keep the last starburst/ring cached on screen until
        // another impact happens to invalidate the canvas.
        if (IsFeedbackActive || IsHomeRunBurstActive || visualExpired)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (!IsFeedbackActive && !IsHomeRunBurstActive)
            return;

        Vector2 impactLocal = ToLocal(_impactWorldPoint);
        if (IsFeedbackActive)
        {
            double elapsed = (Time.GetTicksUsec() - _feedbackStartedUsec) / 1_000_000.0;
            float progress = Mathf.Clamp((float)(elapsed / Profile.RingSeconds), 0.0f, 1.0f);
            float alpha = 1.0f - progress;
            // Ease-out: the shockwave leaves the contact point fast and settles, which is
            // what makes it read as a blast rather than as a growing circle.
            float ringEase = 1.0f - ((1.0f - progress) * (1.0f - progress));
            float radius = Mathf.Lerp(9.0f, 46.0f, ringEase);
            Color ring = new(RingColor, alpha * _impactIntensity);
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

            DrawSpeedLines(impactLocal, elapsed);
            DrawImpactFrame(impactLocal, elapsed);

            // A presentation-only edge jolt: the world/camera and pointer mapping do
            // not move, so it cannot corrupt physics or desktop coordinates. Screen Shake
            // owns it, exactly as it owns the camera-kick lane.
            Rect2 viewport = GetViewportRect();
            float jolt = _effects.ScreenShake
                ? Mathf.Sin(progress * Mathf.Tau * 3.0f) *
                  Profile.CanvasJoltPixels * alpha * _impactIntensity
                : 0.0f;
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

        if (IsHomeRunBurstActive)
        {
            Vector2 burstLocal = ToLocal(LastHomeRunBurstWorldPoint);
            double elapsed = (Time.GetTicksUsec() - _homeRunBurstStartedUsec) / 1_000_000.0;
            float progress = Mathf.Clamp(
                (float)(elapsed / Profile.HomeRunBurstSeconds), 0.0f, 1.0f);
            float alpha = 1.0f - progress;
            float radius = Mathf.Lerp(
                Profile.HomeRunBurstSizePx * 0.45f,
                Profile.HomeRunBurstSizePx,
                progress);
            Color burst = new(
                HomeRunBurstColor,
                alpha * Mathf.Lerp(1.0f, 0.55f, progress));
            DrawCircle(burstLocal, Mathf.Lerp(3.0f, 1.0f, progress), burst);
            for (int ray = 0; ray < 6; ray++)
            {
                Vector2 direction = Vector2.Right.Rotated(ray * Mathf.Tau / 6.0f);
                DrawLine(
                    burstLocal + direction * 4.0f,
                    burstLocal + direction * radius,
                    burst,
                    2.0f,
                    true);
            }
        }
    }

    /// <summary>
    /// The impact frame: a hard white starburst held over the contact point for the first
    /// few ticks, then gone. Its size follows the hit, so a graze pops and a maximum hit
    /// fills the small window.
    /// </summary>
    private void DrawImpactFrame(Vector2 impactLocal, double elapsed)
    {
        if (elapsed >= ImpactFrameSeconds)
            return;

        float progress = Mathf.Clamp((float)(elapsed / ImpactFrameSeconds), 0.0f, 1.0f);
        // Photosensitivity Safe trades the full-white pop for a dim warm one. The shape
        // still lands; only the luminance step does not.
        Color tint = _effects.PhotosensitivitySafe ? SafeImpactFrameColor : ImpactFrameColor;
        float peakAlpha = _effects.PhotosensitivitySafe ? 0.55f : 0.95f;
        var star = new Color(tint, peakAlpha * (1.0f - (progress * progress)) * _impactIntensity);
        float outer = Mathf.Lerp(14.0f, 30.0f, progress) * Mathf.Lerp(0.6f, 1.0f, _impactIntensity);
        float inner = outer * 0.42f;

        var points = new Vector2[StarPoints * 2];
        for (int point = 0; point < points.Length; point++)
        {
            float angle = point * Mathf.Pi / StarPoints;
            points[point] = impactLocal +
                Vector2.Right.Rotated(angle) * ((point % 2 == 0) ? outer : inner);
        }

        DrawColoredPolygon(points, star);
    }

    /// <summary>
    /// Radial speed lines: the second cartoon cue, drawn behind the frame and thinned by
    /// the shared Reduced Particles stride rather than by a rule of its own.
    /// </summary>
    private void DrawSpeedLines(Vector2 impactLocal, double elapsed)
    {
        if (elapsed >= SpeedLineSeconds)
            return;

        float progress = Mathf.Clamp((float)(elapsed / SpeedLineSeconds), 0.0f, 1.0f);
        var line = new Color(SpeedLineColor, (1.0f - progress) * 0.9f * _impactIntensity);
        float near = Mathf.Lerp(16.0f, 34.0f, progress);
        float far = near + Mathf.Lerp(22.0f, 9.0f, progress);
        for (int index = 0; index < SpeedLineCount; index += _effects.ParticleStride)
        {
            // The deterministic fan offset keeps the same hit drawing the same burst.
            Vector2 direction = Vector2.Right.Rotated(
                (index * Mathf.Tau / SpeedLineCount) + (index % 2 == 0 ? 0.0f : 0.14f));
            DrawLine(impactLocal + direction * near, impactLocal + direction * far, line, 2.0f, true);
        }
    }

    private void OnImpact(AcceptedImpact impact)
    {
        // The ring and squash belong to whichever cursor-tethered tool landed the
        // hit, so every tool on that mechanism reads the same way rather than the
        // glove alone having feedback. Attribution, not liveness: a scenario probe
        // striking under a tool's identity still earns the tool's feedback.
        if (!CursorTools.AttributesContent(impact.ContentId))
            return;

        _feedbackStartedUsec = Time.GetTicksUsec();
        _impactWorldPoint = impact.Point;
        _impactNormal = impact.Normal;
        _impactIntensity = Mathf.Clamp(impact.Pain / Profile.MaximumPain, 0.25f, 1.0f);
        IsFeedbackActive = true;
        FeedbackCount++;
        if (impact.SwingEpoch > 0)
        {
            _homeRunBurstStartedUsec = _feedbackStartedUsec;
            LastHomeRunBurstWorldPoint = impact.Point;
            IsHomeRunBurstActive = true;
            HomeRunBurstCount++;
        }
        // Only the collider that actually struck squashes; a live tool of a different
        // identity must not flinch for someone else's hit.
        if (CursorTools.ActiveContentId == impact.ContentId)
            CursorTools.Body?.PulseImpact(impact.Normal, _impactIntensity, Profile.GloveSquashSeconds);
        QueueRedraw();

        bool homeRunFreeze = HitLag.IsActive;
        if (homeRunFreeze)
        {
            // The owner-confirmed whole-game home-run freeze wins over the
            // glove's global slow-time envelope. This also unwinds an envelope
            // already in progress so two time-control effects never compound.
            StopHitStop();
        }

        // Reduced Motion turns the slow-time envelope off outright: it is the one layer
        // here that moves the whole game rather than pixels around the contact point.
        if (!homeRunFreeze && !_effects.ReducedMotion &&
            (impact.Pain + 0.0001f >= Profile.MaximumPain || impact.KnockoutTriggered) &&
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

    private void OnHitLagStarted(SwingHitLagStarted started) => StopHitStop();

    private void StopHitStop()
    {
        if (!IsHitStopActive)
        {
            return;
        }

        Engine.TimeScale = _resumeScale;
        LastAppliedHitStopScale = 1.0f;
        IsHitStopActive = false;
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

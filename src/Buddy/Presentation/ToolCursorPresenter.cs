using System;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>
/// Original vector Brush and Tickle feather rendered beneath the visible OS cursor.
/// This is presentation-only: it follows the routed pointer position instantly
/// and never participates in collision, care progress, or physics.
/// </summary>
[GlobalClass]
public partial class ToolCursorPresenter : Node2D
{
    private const int SparkleCapacity = 8;
    private const double SparkleEmissionSeconds = 0.055;
    private const double SparkleLifetimeSeconds = 0.28;
    private static readonly Color Outline = new("553a33");
    private static readonly Color BrushWood = new("f0b969");
    private static readonly Color BrushWoodShade = new("d09246");
    private static readonly Color BrushStrap = new("5b4433");
    private static readonly Color BrushBristle = new("6b5340");
    private static readonly Color FeatherFill = new("f5f1df");
    private static readonly Color FeatherShade = new("d9cfae");
    private static readonly Color FeatherShaft = new("8a6544");
    private static readonly Color StickBody = new("d6a94a");
    private static readonly Color StickHighlight = new("f7dc93");
    private static readonly Color Ferrule = new("b9862c");


    private static readonly Color SparkleCore = new("fff4a8");
    private static readonly Color SparkleEdge = new("f6a623");

    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;

    private readonly SparkleParticle[] _sparkles = new SparkleParticle[SparkleCapacity];
    private ToolId _tool;
    private bool _held;
    private double _phase;
    private double _sparkleEmissionAccumulator;
    private int _nextSparkle;
    private CareToolSway _sway;
    private bool _legacyEnabled = true;

    public ToolId Tool => _tool;
    /// <summary>Legacy test oracle: true for either care-tool cursor while held.</summary>
    public bool IsHandVisible => Visible;
    public bool IsTickleFeatherVisible => Visible && _held && _tool == ToolId.Tickle;
    public bool IsInitialized { get; private set; }
    public bool IsFavoriteSparkleActive { get; private set; }
    public int ActiveSparkleCount { get; private set; }
    public int SparkleEmissionCount { get; private set; }

    public override void _Ready()
    {
        ZAsRelative = false;
        ZIndex = 200;
        Visible = false;
    }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(CareStroke) || !CareStroke.IsInitialized)
            throw new InvalidOperationException("ToolCursorPresenter requires an initialized CareStrokeComponent.");
        IsInitialized = true;
    }

    public void SetPointerState(ToolId tool, Vector2 worldPosition, bool held)
    {
        _tool = tool;
        _held = held;
        GlobalPosition = worldPosition;
        Visible = held && tool is ToolId.Pet or ToolId.Tickle;
        QueueRedraw();
    }

    /// <summary>
    /// One care tool per cursor: this vector drawing and <c>CareToolVisual3D</c> are the same
    /// tool seen two ways, never both at once. The node itself stays live either way — the
    /// favourite-spot sparkles are drawn here in both presentations.
    /// </summary>
    public void SetLegacyVisualEnabled(bool enabled)
    {
        _legacyEnabled = enabled;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            ClearSparkles();
            _sway.Reset();
            return;
        }

        _phase += delta;
        _sway.Tick(GlobalPosition, CareStroke.FeatherAngle, CareStroke.IsWiggling, delta);
        UpdateSparkles(delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_held)
            return;

        if (_legacyEnabled)
        {
            if (_tool == ToolId.Pet)
                DrawBrush();
            else if (_tool == ToolId.Tickle)
                DrawTickleFeather();
        }

        DrawSparkles();
    }

    /// <summary>
    /// A horse/body brush: oval wooden block, leather hand strap across the back, bristles
    /// underneath (owner reference 2026-08-19 — the tool is the Brush, not a bare hand).
    /// </summary>
    private void DrawBrush()
    {
        float rub = Mathf.Sin((float)_phase * 18.0f) * 3.0f;
        DrawSetTransform(new Vector2(10.0f + rub, 12.0f), -0.35f, Vector2.One);

        const float halfWidth = 17.0f;
        const float halfHeight = 7.0f;

        // Bristles first so the block sits over their roots.
        const int bristleCount = 9;
        for (int index = 0; index < bristleCount; index++)
        {
            float t = (index + 0.5f) / bristleCount;
            float x = Mathf.Lerp(-halfWidth + 2.0f, halfWidth - 2.0f, t);
            // Shorter at the ends so the tuft follows the block's curve.
            float shoulder = 1.0f - Mathf.Abs((t * 2.0f) - 1.0f);
            float length = 6.0f + shoulder * 4.0f;
            var root = new Vector2(x, halfHeight - 1.0f);
            var end = new Vector2(x, halfHeight - 1.0f + length);
            DrawLine(root, end, Outline, 4.6f, true);
            DrawLine(root, end, BrushBristle, 3.0f, true);
            DrawCircle(end, 1.5f, BrushBristle, true, -1.0f, true);
        }

        // Wooden block, drawn as a squat capsule so it reads as an oval from the front.
        var left = new Vector2(-halfWidth + halfHeight, 0.0f);
        var right = new Vector2(halfWidth - halfHeight, 0.0f);
        DrawLine(left, right, Outline, (halfHeight * 2.0f) + 3.0f, true);
        DrawLine(left, right, BrushWood, halfHeight * 2.0f, true);
        DrawLine(
            left + Vector2.Down * 2.5f,
            right + Vector2.Down * 2.5f,
            BrushWoodShade,
            halfHeight * 0.7f,
            true);

        // Leather hand strap across the back.
        DrawLine(new Vector2(-5.5f, -1.0f), new Vector2(5.5f, -1.0f), Outline, 11.0f, true);
        DrawLine(new Vector2(-5.0f, -1.0f), new Vector2(5.0f, -1.0f), BrushStrap, 8.0f, true);
    }


    /// <summary>
    /// A feather duster: brass stick from the grip, ferrule, then a flat vane with a visible
    /// rachis. Local +X is the stick, so the whole drawing rotates with the aim.
    /// </summary>
    private void DrawTickleFeather()
    {
        // A slow idle breath under the sway, so a parked feather is not dead still.
        float breath = Mathf.Sin((float)_phase * 3.2f) * 0.035f;
        DrawSetTransform(
            CareToolGeometry.GripOffset,
            CareStroke.FeatherAngle + _sway.Angle + breath,
            Vector2.One);

        float stick = CareToolGeometry.StickLength;
        float plume = CareToolGeometry.PlumeLength;
        float wide = CareToolGeometry.PlumeHalfWidth;
        var grip = Vector2.Zero;
        var collar = new Vector2(stick, 0.0f);

        // Brass stick, drawn dark-first so it stays readable over any wallpaper.
        DrawLine(grip, collar, Outline, 5.0f, true);
        DrawLine(grip, collar, StickBody, 3.0f, true);
        DrawLine(grip, collar, StickHighlight, 1.0f, true);
        DrawCircle(grip, 2.6f, Ferrule, true, -1.0f, true);
        DrawArc(grip, 2.6f, 0, Mathf.Tau, 12, Outline, 1.4f, true);

        // Tapered ferrule where the vane is crimped onto the stick.
        var root = new Vector2(stick + CareToolGeometry.FerruleLength, 0.0f);
        DrawLine(collar - new Vector2(3.0f, 0.0f), root, Outline, 7.0f, true);
        DrawLine(collar - new Vector2(3.0f, 0.0f), root, Ferrule, 4.5f, true);

        // The vane bends further than the stick, so it lags behind on a flick.
        float lag = Mathf.Clamp(-_sway.Velocity * 3.0f, -11.0f, 11.0f);
        var tip = new Vector2(root.X + plume, lag);

        // Barbs: pairs of strokes swept back from the rachis, longest a third of the way up.
        const int barbCount = 13;
        for (int index = 0; index < barbCount; index++)
        {
            float t = (index + 0.5f) / barbCount;
            Vector2 spine = root.Lerp(tip, t);
            float width = wide * VaneWidth(t);
            float sweep = 3.0f + (t * 5.0f);
            var left = new Vector2(spine.X - sweep, spine.Y - width);
            var right = new Vector2(spine.X - sweep, spine.Y + width);
            DrawLine(spine, left, Outline, 3.8f, true);
            DrawLine(spine, left, index % 2 == 0 ? FeatherFill : FeatherShade, 2.2f, true);
            DrawLine(spine, right, Outline, 3.8f, true);
            DrawLine(spine, right, index % 2 == 0 ? FeatherShade : FeatherFill, 2.2f, true);
        }

        // Rachis last, over the barb roots, so the vane has a spine.
        DrawLine(root, tip, Outline, 3.2f, true);
        DrawLine(root, tip, FeatherShaft, 1.5f, true);
        DrawCircle(tip, 1.8f, FeatherFill, true, -1.0f, true);
    }

    /// <summary>
    /// Vane half-width as a fraction of the widest point: narrow at the quill, widest a third
    /// of the way up, drawn to a point. The same silhouette the 3D vane is lathed from.
    /// </summary>
    private static float VaneWidth(float t) => t < 0.35f
        ? Mathf.Lerp(0.30f, 1.0f, t / 0.35f)
        : Mathf.Lerp(1.0f, 0.05f, (t - 0.35f) / 0.65f);

    private void UpdateSparkles(double delta)
    {
        IsFavoriteSparkleActive =
            IsInitialized &&
            _tool == ToolId.Pet &&
            _held &&
            CareStroke.IsPetRubbing &&
            CareStroke.ContactPart == CareStroke.FavoritePart;

        if (!IsFavoriteSparkleActive)
        {
            ClearSparkles();
            return;
        }

        _sparkleEmissionAccumulator += delta;
        while (_sparkleEmissionAccumulator >= SparkleEmissionSeconds)
        {
            _sparkleEmissionAccumulator -= SparkleEmissionSeconds;
            EmitSparkle();
        }

        ActiveSparkleCount = 0;
        for (int index = 0; index < _sparkles.Length; index++)
        {
            ref SparkleParticle sparkle = ref _sparkles[index];
            if (!sparkle.Active)
                continue;

            sparkle.Age += delta;
            if (sparkle.Age >= sparkle.Lifetime)
            {
                sparkle.Active = false;
                continue;
            }

            sparkle.Offset += sparkle.Velocity * (float)delta;
            ActiveSparkleCount++;
        }
    }

    private void EmitSparkle()
    {
        int sequence = SparkleEmissionCount++;
        float angle = sequence * 2.3999632f;
        float radius = 12.0f + (sequence % 3) * 3.0f;
        Vector2 direction = Vector2.FromAngle(angle);
        _sparkles[_nextSparkle] = new SparkleParticle
        {
            Active = true,
            Offset = new Vector2(10.0f, 10.0f) + direction * radius,
            Velocity = direction * (12.0f + (sequence % 2) * 4.0f) + Vector2.Up * 9.0f,
            Age = 0.0,
            Lifetime = SparkleLifetimeSeconds,
            Rotation = angle,
        };
        _nextSparkle = (_nextSparkle + 1) % _sparkles.Length;
    }

    private void DrawSparkles()
    {
        for (int index = 0; index < _sparkles.Length; index++)
        {
            SparkleParticle sparkle = _sparkles[index];
            if (!sparkle.Active)
                continue;

            float progress = (float)(sparkle.Age / sparkle.Lifetime);
            float alpha = 1.0f - progress;
            float size = Mathf.Lerp(3.5f, 1.0f, progress);
            Color edge = new(SparkleEdge, alpha);
            Color core = new(SparkleCore, alpha);
            Vector2 horizontal = Vector2.FromAngle(sparkle.Rotation) * size;
            Vector2 vertical = horizontal.Rotated(Mathf.Pi * 0.5f);
            DrawLine(sparkle.Offset - horizontal, sparkle.Offset + horizontal, edge, 2.0f, true);
            DrawLine(sparkle.Offset - vertical, sparkle.Offset + vertical, edge, 2.0f, true);
            DrawCircle(sparkle.Offset, Mathf.Max(0.7f, size * 0.32f), core, true, -1.0f, true);
        }
    }

    private void ClearSparkles()
    {
        IsFavoriteSparkleActive = false;
        ActiveSparkleCount = 0;
        _sparkleEmissionAccumulator = 0.0;
        for (int index = 0; index < _sparkles.Length; index++)
            _sparkles[index].Active = false;
    }

    private struct SparkleParticle
    {
        public bool Active;
        public Vector2 Offset;
        public Vector2 Velocity;
        public double Age;
        public double Lifetime;
        public float Rotation;
    }
}
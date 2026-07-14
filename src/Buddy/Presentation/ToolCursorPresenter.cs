using System;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation;

/// <summary>
/// Original vector Pet/Tickle hands rendered beneath the visible OS cursor.
/// This is presentation-only: it follows the routed pointer position instantly
/// and never participates in collision, care progress, or physics.
/// </summary>
[GlobalClass]
public partial class ToolCursorPresenter : Node2D
{
    private const int SparkleCapacity = 8;
    private const double SparkleEmissionSeconds = 0.055;
    private const double SparkleLifetimeSeconds = 0.28;
    private static readonly Color Fill = new("f4d8b5");
    private static readonly Color Outline = new("553a33");
    private static readonly Color SparkleCore = new("fff4a8");
    private static readonly Color SparkleEdge = new("f6a623");

    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;

    private readonly SparkleParticle[] _sparkles = new SparkleParticle[SparkleCapacity];
    private ToolId _tool;
    private bool _held;
    private double _phase;
    private double _sparkleEmissionAccumulator;
    private int _nextSparkle;

    public ToolId Tool => _tool;
    public bool IsHandVisible => Visible;
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

    public override void _Process(double delta)
    {
        if (!Visible)
        {
            ClearSparkles();
            return;
        }

        _phase += delta;
        UpdateSparkles(delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_held)
            return;

        if (_tool == ToolId.Pet)
            DrawPetHand();
        else if (_tool == ToolId.Tickle)
            DrawTickleHand();

        DrawSparkles();
    }

    private void DrawPetHand()
    {
        float rub = Mathf.Sin((float)_phase * 18.0f) * 3.0f;
        DrawSetTransform(new Vector2(10.0f + rub, 12.0f), -0.35f, Vector2.One);
        DrawCircle(Vector2.Zero, 9.0f, Fill, true, -1.0f, true);
        DrawArc(Vector2.Zero, 9.0f, 0, Mathf.Tau, 24, Outline, 2.0f, true);
        for (int finger = 0; finger < 4; finger++)
        {
            float y = -7.0f + finger * 4.5f;
            DrawLine(new Vector2(-3.0f, y), new Vector2(-16.0f, y - 4.0f), Outline, 4.0f, true);
            DrawLine(new Vector2(-3.0f, y), new Vector2(-16.0f, y - 4.0f), Fill, 2.0f, true);
        }
        DrawLine(new Vector2(6.0f, 6.0f), new Vector2(16.0f, 13.0f), Outline, 5.0f, true);
        DrawLine(new Vector2(6.0f, 6.0f), new Vector2(16.0f, 13.0f), Fill, 3.0f, true);
    }

    private void DrawTickleHand()
    {
        float wiggle = Mathf.Sin((float)_phase * 28.0f) * 0.22f;
        DrawSetTransform(new Vector2(10.0f, 12.0f), 0.15f, Vector2.One);
        DrawCircle(Vector2.Zero, 8.0f, Fill, true, -1.0f, true);
        DrawArc(Vector2.Zero, 8.0f, 0, Mathf.Tau, 24, Outline, 2.0f, true);
        for (int finger = 0; finger < 4; finger++)
        {
            float angle = -2.65f + finger * 0.35f + (finger % 2 == 0 ? wiggle : -wiggle);
            Vector2 start = Vector2.FromAngle(angle) * 5.0f;
            Vector2 bend = start + Vector2.FromAngle(angle - 0.35f) * 8.0f;
            Vector2 end = bend + Vector2.FromAngle(angle + 0.2f) * 8.0f;
            DrawPolyline(new[] { start, bend, end }, Outline, 5.0f, true);
            DrawPolyline(new[] { start, bend, end }, Fill, 2.5f, true);
        }
        DrawLine(new Vector2(5.0f, 5.0f), new Vector2(15.0f, 12.0f), Outline, 5.0f, true);
        DrawLine(new Vector2(5.0f, 5.0f), new Vector2(15.0f, 12.0f), Fill, 3.0f, true);
    }

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

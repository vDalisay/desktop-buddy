using System;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Capture-only paint punctuation. The canvas remains the authority for whether pixels changed;
/// this partial exposes only presentation observations and hosts one bounded, non-physical overlay.
/// </summary>
public partial class PaintCanvasControl
{
    internal bool IsPaintingForPresentation => _painting;
    internal Vector2 PaintPointerForPresentation => _lastPointer;

    public override void _EnterTree()
    {
        var droplets = new PaintDropletOverlay
        {
            Name = "PaintDropletOverlay",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        droplets.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(droplets);
        droplets.Initialize(this);
    }
}

/// <summary>
/// Small compatibility-renderer-safe paint droplets. No particles server, rigid bodies, collision,
/// persistence, or input are involved. A fixed 24-slot array is the complete live-particle budget.
/// </summary>
internal sealed partial class PaintDropletOverlay : Control
{
    private const int MaximumDroplets = 24;
    private const int ReducedMaximumDroplets = 8;
    private const float NormalSampleInterval = 1.0f / 30.0f;
    private const float ReducedSampleInterval = 0.10f;
    private const float MinimumSampleDistance = 4.0f;

    private readonly Droplet[] _droplets = new Droplet[MaximumDroplets];
    private PaintCanvasControl? _canvas;
    private CharacterEditorHost? _host;
    private long _lastRevision;
    private float _sampleCooldown;
    private Vector2 _lastSamplePosition;
    private bool _hasSamplePosition;
    private int _nextSlot;
    private uint _noiseState = 0xA341316Cu;

    public void Initialize(PaintCanvasControl canvas)
    {
        _canvas = canvas;
        _lastRevision = TotalRevision(canvas.Workspace);
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Node? ancestor = GetParent();
        while (ancestor is not null && ancestor is not CharacterEditorHost)
            ancestor = ancestor.GetParent();
        _host = ancestor as CharacterEditorHost;
    }

    public override void _Process(double delta)
    {
        if (_canvas is null || !GodotObject.IsInstanceValid(_canvas))
            return;

        float dt = (float)Math.Max(0.0, delta);
        _sampleCooldown = Math.Max(0.0f, _sampleCooldown - dt);
        AdvanceDroplets(dt);

        long revision = TotalRevision(_canvas.Workspace);
        bool changed = revision != _lastRevision;
        _lastRevision = revision;
        if (!changed || !_canvas.Visible || !_canvas.IsPaintingForPresentation ||
            _canvas.PanToolActive || _canvas.EyedropperToolActive ||
            _canvas.Workspace.SelectedTool == PaintTool.Eraser)
        {
            QueueRedraw();
            return;
        }

        bool reduced = _host?.ReducedParticlesForCaptureFx ?? false;
        float interval = reduced ? ReducedSampleInterval : NormalSampleInterval;
        Vector2 pointer = _canvas.PaintPointerForPresentation;
        if (_sampleCooldown > 0.0f ||
            (_hasSamplePosition && pointer.DistanceTo(_lastSamplePosition) < MinimumSampleDistance))
        {
            QueueRedraw();
            return;
        }

        _sampleCooldown = interval;
        _lastSamplePosition = pointer;
        _hasSamplePosition = true;
        Emit(pointer, PaintColorToGodot(_canvas.Workspace.SelectedColor), reduced);
        QueueRedraw();
    }

    public override void _Draw()
    {
        for (int index = 0; index < _droplets.Length; index++)
        {
            Droplet droplet = _droplets[index];
            if (!droplet.Live)
                continue;

            float life = 1.0f - Mathf.Clamp(droplet.Age / droplet.Lifetime, 0.0f, 1.0f);
            Color color = new(droplet.Color.R, droplet.Color.G, droplet.Color.B, life * 0.78f);
            DrawCircle(droplet.Position, droplet.Radius * (0.65f + life * 0.35f), color);
        }
    }

    private void Emit(Vector2 origin, Color color, bool reduced)
    {
        int activeBudget = reduced ? ReducedMaximumDroplets : MaximumDroplets;
        int slot = FindReusableSlot(activeBudget);
        float sideways = SignedNoise() * 34.0f;
        float upward = -22.0f - (PositiveNoise() * 30.0f);
        float radius = 2.0f + (PositiveNoise() * 3.0f);
        float lifetime = 0.22f + (PositiveNoise() * 0.18f);
        _droplets[slot] = new Droplet(
            Live: true,
            Position: origin,
            Velocity: new Vector2(sideways, upward),
            Color: color,
            Radius: radius,
            Age: 0.0f,
            Lifetime: lifetime);
        _nextSlot = (slot + 1) % activeBudget;
    }

    private void AdvanceDroplets(float delta)
    {
        for (int index = 0; index < _droplets.Length; index++)
        {
            Droplet droplet = _droplets[index];
            if (!droplet.Live)
                continue;

            float age = droplet.Age + delta;
            if (age >= droplet.Lifetime)
            {
                _droplets[index] = default;
                continue;
            }

            Vector2 velocity = droplet.Velocity + (Vector2.Down * (105.0f * delta));
            _droplets[index] = droplet with
            {
                Position = droplet.Position + (velocity * delta),
                Velocity = velocity,
                Age = age,
            };
        }
    }

    private int FindReusableSlot(int budget)
    {
        for (int offset = 0; offset < budget; offset++)
        {
            int slot = (_nextSlot + offset) % budget;
            if (!_droplets[slot].Live)
                return slot;
        }
        return _nextSlot % budget;
    }

    private float PositiveNoise()
    {
        _noiseState ^= _noiseState << 13;
        _noiseState ^= _noiseState >> 17;
        _noiseState ^= _noiseState << 5;
        return (_noiseState & 0x00FFFFFFu) / 16777215.0f;
    }

    private float SignedNoise() => (PositiveNoise() * 2.0f) - 1.0f;

    private static long TotalRevision(PaintWorkspace workspace)
    {
        long total = 0;
        foreach (PaintSurface surface in workspace.Surfaces.Values)
            total += surface.Revision;
        return total;
    }

    private static Color PaintColorToGodot(PaintColor color) =>
        Color.Color8(color.R, color.G, color.B, byte.MaxValue);

    private readonly record struct Droplet(
        bool Live,
        Vector2 Position,
        Vector2 Velocity,
        Color Color,
        float Radius,
        float Age,
        float Lifetime);
}

using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// M3.6 Task 5 composed dynamic face: translates the semantic face contract
/// (<c>Reactions.CurrentFace</c> — unchanged, prime invariant 3) plus the overlays
/// (seeded blink, eat chew loop, Task 4 pupil quanta) into a <see cref="FaceRenderState"/>
/// and re-renders the face texture ON CHANGE ONLY. Composition logic is engine-free
/// (<see cref="FaceComposer"/>, <see cref="BlinkModel"/>, <see cref="ChewCycle"/>); this
/// node samples real semantics, counts the blink/chew clocks in ROUTED ticks (a paused
/// laboratory holds the face still), and owns the GPU path — a small offscreen
/// <see cref="SubViewport"/> a painter Control draws into, guarded off entirely when
/// headless so scenarios assert only the semantic oracles
/// (<see cref="LastComposedState"/>, <see cref="RenderCount"/>).
///
/// Style is selectable per the mockup-gate decision (Soft Oval ships; ink-dots and
/// bean+blush are reserved shop cosmetics), but only <see cref="SoftOvalFacePainter"/>
/// exists in this slice.
/// </summary>
[GlobalClass]
public partial class FaceCompositor : Node
{
    // Distinct stream per consumer family (IRandomSource contract): blinks must perturb
    // neither autonomy nor the facing/glance streams.
    private const ulong BlinkStreamSalt = 0xB11B_FACE_2026_0720UL;

    /// <summary>Render-target square size in pixels; the plate spans PlateWorldSize world units.</summary>
    public const int TextureSize = 200;

    /// <summary>World size of the face plate quad the texture maps onto (see the presenter).</summary>
    public const float PlateWorldSize = 40.0f;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public BuddyReactionComponent Reactions { get; set; } = null!;
    [Export] public ActivityAnimator Activities { get; set; } = null!;
    [Export] public BuddyExpressionProfile Profile { get; set; } = null!;
    /// <summary>The ink authority: the face draws in the same look the presenter renders
    /// (lab_buddy_look.tres OutlineColor), so face and outline ink can never diverge.</summary>
    [Export] public BuddyVisualProfile VisualProfile { get; set; } = null!;
    /// <summary>Optional: without look-at the pupils simply stay centered.</summary>
    [Export] public HeadLookAtComponent? HeadLookAt { get; set; }

    private BlinkModel _blink = null!;
    private long _lastRoutedTick;
    private bool _hasComposed;
    private SubViewport? _viewport;
    private FacePainterControl? _painter;

    public bool IsInitialized { get; private set; }
    public FaceStyleId Style { get; private set; } = FaceStyleId.SoftOval;

    /// <summary>The last composed render key — the scenarios' semantic oracle.</summary>
    public FaceRenderState LastComposedState { get; private set; }

    /// <summary>How many re-renders have been requested (bounded by state changes).</summary>
    public int RenderCount { get; private set; }

    /// <summary>The face texture, or null when headless (GPU path guarded off).</summary>
    public Texture2D? OutputTexture => _viewport?.GetTexture();

    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Reactions) ||
            !GodotObject.IsInstanceValid(Activities) || !Activities.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile) ||
            !GodotObject.IsInstanceValid(VisualProfile) ||
            !GodotObject.IsInstanceValid(VisualProfile.Look))
        {
            throw new InvalidOperationException("FaceCompositor dependencies are incomplete.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy expression profile: {string.Join("; ", errors)}");
        }

        // The catalog must cover every string the resolver can produce; failing at
        // composition time (mid-play, on a rare face) would be far worse than failing loud
        // at boot. The list lives beside the domain catalog; this guards the translation.
        foreach (string face in FaceExpressionMap.Faces)
        {
            if (!FaceExpressionMap.TryResolve(face, out _))
            {
                throw new InvalidOperationException($"Face expression map misses '{face}'.");
            }
        }

        Reseed(Buddy.AutonomousMotion.Seed);
        Buddy.AutonomyReseeded += Reseed;
        _lastRoutedTick = Buddy.RoutedTicks;

        // GPU path only where a GPU exists. Headless runs keep the full semantic pipeline
        // (compose, blink, render-count) with no viewport, so scenario checks are identical
        // in both environments.
        if (DisplayServer.GetName() != "headless")
        {
            BuildRenderTarget();
        }

        IsInitialized = true;
        Evaluate();
    }

    public override void _ExitTree()
    {
        if (!IsInitialized)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(Buddy))
        {
            Buddy.AutonomyReseeded -= Reseed;
        }
    }

    /// <summary>Rebuilds the blink stream from the shared seed (own salted stream).</summary>
    public void Reseed(ulong seed) => _blink = new BlinkModel(
        new SeededRandomSource(seed ^ BlinkStreamSalt),
        Profile.ToData().ToBlinkParameters());

    /// <summary>
    /// Samples semantics, advances the blink/chew clocks by the routed ticks since the last
    /// call, and re-renders if and only if the composed state changed. Called by the
    /// presenter once per rendered frame; allocation-free.
    /// </summary>
    public FaceRenderState Evaluate()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("FaceCompositor used before initialization.");
        }

        long now = Buddy.RoutedTicks;
        int ticksElapsed = (int)Math.Clamp(now - _lastRoutedTick, 0, int.MaxValue);
        _lastRoutedTick = now;

        string face = Reactions.CurrentFace;
        FaceFeaturePose pose = FaceExpressionMap.Resolve(face);
        _blink.Update(!pose.EyesBlinkable, ticksElapsed);

        // The eat chew loop: the activity selector already suppresses Eat to None in
        // Tracking, and the composer stands the overlay down under reaction-priority faces
        // (the same list that owns the head in look-at).
        bool chewActive = Activities.Current == ActivityId.Eat;
        int chewFrame = ChewCycle.FrameAt(now, Profile.ChewCycleTicks);
        Vector2 pupils = HeadLookAt is { IsInitialized: true }
            ? HeadLookAt.PupilOffset
            : Vector2.Zero;

        FaceRenderState state = FaceComposer.Compose(
            pose,
            _blink.EyesClosed,
            chewActive,
            chewFrame,
            Profile.SuppressesLookAt(face),
            pupils.X,
            pupils.Y);

        if (!_hasComposed || state != LastComposedState)
        {
            _hasComposed = true;
            LastComposedState = state;
            RenderCount++;
            Repaint(state);
        }

        return state;
    }

    private void BuildRenderTarget()
    {
        _viewport = new SubViewport
        {
            Name = "FaceViewport",
            Size = new Vector2I(TextureSize, TextureSize),
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
        };
        _painter = new FacePainterControl
        {
            Name = "FacePainter",
            Painter = new SoftOvalFacePainter(VisualProfile.Look.OutlineColor),
            Size = new Vector2(TextureSize, TextureSize),
        };
        _viewport.AddChild(_painter);
        AddChild(_viewport);
    }

    private void Repaint(in FaceRenderState state)
    {
        if (_painter is null || _viewport is null)
        {
            return;
        }

        _painter.State = state;
        _painter.QueueRedraw();
        // Render exactly one frame per state change (the shipping form of the plan's
        // "re-render on change only" rule).
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
}

/// <summary>Draws the current <see cref="FaceRenderState"/> with the active style painter.</summary>
public partial class FacePainterControl : Control
{
    public IFaceStylePainter Painter { get; set; } = null!;
    public FaceRenderState State { get; set; }

    public override void _Draw() => Painter?.Paint(this, State);
}

/// <summary>One face art style: draws a composed state onto the painter control.</summary>
public interface IFaceStylePainter
{
    FaceStyleId Style { get; }

    void Paint(Control canvas, in FaceRenderState state);
}

/// <summary>
/// The accepted default face art (mockup gate, DECISIONS.md 2026-07-20): filled vertical
/// oval eyes with a white highlight that doubles as the pupil, subtle arc brows, rounded
/// ink mouths. Geometry is in face units — world pixels on the 40x40 plate, x right /
/// y up, origin at head center — scaled 5x into the 200x200 render target, matching the
/// accepted mockup art exactly.
/// </summary>
public sealed class SoftOvalFacePainter : IFaceStylePainter
{
    private const float Scale = FaceCompositor.TextureSize / FaceCompositor.PlateWorldSize;
    private static readonly Color White = new(1.0f, 1.0f, 1.0f);

    private readonly Color _ink;

    public SoftOvalFacePainter(Color ink) => _ink = ink;

    public FaceStyleId Style => FaceStyleId.SoftOval;

    public void Paint(Control canvas, in FaceRenderState state)
    {
        DrawEyes(canvas, state);
        DrawBrows(canvas, state.Brows);
        DrawMouth(canvas, state.Mouth);
    }

    private void DrawEyes(Control canvas, in FaceRenderState state)
    {
        if (state.Blinking)
        {
            // Closed lids: gentle downward bows where the ovals were.
            LidArc(canvas, -8.0f, 3.5f);
            LidArc(canvas, 8.0f, 3.5f);
            return;
        }

        switch (state.Eyes)
        {
            case FaceEyePose.Open:
                PupilOval(canvas, -8.0f, 3.5f, 2.6f, 4.0f, state.PupilX, state.PupilY);
                PupilOval(canvas, 8.0f, 3.5f, 2.6f, 4.0f, state.PupilX, state.PupilY);
                break;
            case FaceEyePose.Narrow:
                PupilOval(canvas, -8.0f, 3.0f, 2.6f, 2.4f, state.PupilX, state.PupilY);
                PupilOval(canvas, 8.0f, 3.0f, 2.6f, 2.4f, state.PupilX, state.PupilY);
                break;
            case FaceEyePose.Wide:
                WideEye(canvas, -8.0f, 3.5f, state.PupilX, state.PupilY);
                WideEye(canvas, 8.0f, 3.5f, state.PupilX, state.PupilY);
                break;
            case FaceEyePose.HappyArc:
                HappyArc(canvas, -8.0f, 3.5f);
                HappyArc(canvas, 8.0f, 3.5f);
                break;
            case FaceEyePose.Scrunch:
                Scrunch(canvas, -8.0f, 3.5f);
                Scrunch(canvas, 8.0f, 3.5f);
                break;
            case FaceEyePose.Cross:
                CrossEye(canvas, -8.0f, 3.5f);
                CrossEye(canvas, 8.0f, 3.5f);
                break;
        }
    }

    private void DrawBrows(Control canvas, FaceBrowPose brows)
    {
        switch (brows)
        {
            case FaceBrowPose.Neutral:
                BrowArc(canvas, -8.0f, 8.3f);
                BrowArc(canvas, 8.0f, 8.3f);
                break;
            case FaceBrowPose.Raised:
                BrowArc(canvas, -8.0f, 10.3f);
                BrowArc(canvas, 8.0f, 10.3f);
                break;
            case FaceBrowPose.AngledIn:
                // Angry: outer end high, inner end pulled down toward the nose.
                BrowStroke(canvas, -8.0f, 9.0f, innerDrop: 1.4f);
                BrowStroke(canvas, 8.0f, 9.0f, innerDrop: 1.4f);
                break;
            case FaceBrowPose.Worried:
                // Worry slants the opposite way: inner end raised.
                BrowStroke(canvas, -8.0f, 9.0f, innerDrop: -1.2f);
                BrowStroke(canvas, 8.0f, 9.0f, innerDrop: -1.2f);
                break;
        }
    }

    private void DrawMouth(Control canvas, FaceMouthPose mouth)
    {
        switch (mouth)
        {
            case FaceMouthPose.Flat:
                Stroke(canvas, -2.5f, -7.0f, 2.5f, -7.0f, 2.0f);
                break;
            case FaceMouthPose.Smile:
                SmileArc(canvas, 0.0f, -6.5f, 3.6f, 1.8f);
                break;
            case FaceMouthPose.OpenSmile:
                OpenSmile(canvas, 0.0f, -5.0f, 4.0f);
                break;
            case FaceMouthPose.CatSmile:
                SmileArc(canvas, -1.8f, -6.8f, 2.0f, 1.6f);
                SmileArc(canvas, 1.8f, -6.8f, 2.0f, 1.6f);
                break;
            case FaceMouthPose.Frown:
                FrownArc(canvas, 0.0f, -8.5f, 4.0f, 2.0f);
                break;
            case FaceMouthPose.Squiggle:
                Stroke(canvas, -3.0f, -7.0f, -1.0f, -5.8f, 1.8f);
                Stroke(canvas, -1.0f, -5.8f, 1.0f, -8.2f, 1.8f);
                Stroke(canvas, 1.0f, -8.2f, 3.0f, -7.0f, 1.8f);
                break;
            case FaceMouthPose.SmallO:
                Ring(canvas, 0.0f, -7.0f, 2.0f, 1.6f);
                break;
            case FaceMouthPose.Slant:
                Stroke(canvas, -2.5f, -7.6f, 2.5f, -6.4f, 2.0f);
                break;
            case FaceMouthPose.ChewOpen:
                Ring(canvas, 0.0f, -6.6f, 2.6f, 1.8f);
                break;
            case FaceMouthPose.ChewClosed:
                Stroke(canvas, -2.2f, -7.2f, 2.2f, -7.2f, 2.0f);
                break;
        }
    }

    // --- feature pieces (face units: x right, y up) ----------------------------------

    private void PupilOval(
        Control canvas, float x, float y, float rx, float ry, float pupilX, float pupilY)
    {
        FillEllipse(canvas, x, y, rx, ry, _ink);
        // The white highlight doubles as the pupil: its rest pose is the accepted mockup
        // highlight, and the Task 4 quantized offset slides it inside the oval.
        canvas.DrawCircle(
            P(x - 0.8f + (pupilX * 1.1f), y + 1.3f + (pupilY * 1.1f)),
            S(0.9f), White, filled: true, antialiased: true);
    }

    private void WideEye(Control canvas, float x, float y, float pupilX, float pupilY)
    {
        canvas.DrawCircle(P(x, y), S(3.6f), White, filled: true, antialiased: true);
        canvas.DrawCircle(P(x, y), S(3.6f), _ink, filled: false, width: S(0.5f), antialiased: true);
        canvas.DrawCircle(
            P(x + (pupilX * 1.6f), y + (pupilY * 1.6f)),
            S(1.4f), _ink, filled: true, antialiased: true);
    }

    private void HappyArc(Control canvas, float x, float y) =>
        canvas.DrawArc(P(x, y - 1.4f), S(3.6f),
            Mathf.Pi + 0.35f, Mathf.Tau - 0.35f, 24, _ink, S(2.2f), antialiased: true);

    private void LidArc(Control canvas, float x, float y) =>
        canvas.DrawArc(P(x, y + 1.4f), S(3.2f),
            0.45f, Mathf.Pi - 0.45f, 24, _ink, S(1.8f), antialiased: true);

    private void Scrunch(Control canvas, float x, float y)
    {
        float tip = x < 0.0f ? x + 2.2f : x - 2.2f;
        float back = x < 0.0f ? x - 2.6f : x + 2.6f;
        Stroke(canvas, back, y + 2.6f, tip, y, 2.2f);
        Stroke(canvas, back, y - 2.6f, tip, y, 2.2f);
    }

    private void CrossEye(Control canvas, float x, float y)
    {
        Stroke(canvas, x - 2.2f, y + 2.2f, x + 2.2f, y - 2.2f, 1.8f);
        Stroke(canvas, x - 2.2f, y - 2.2f, x + 2.2f, y + 2.2f, 1.8f);
    }

    private void BrowArc(Control canvas, float x, float y) =>
        canvas.DrawArc(P(x, y - 1.2f), S(3.0f),
            Mathf.Pi + 0.55f, Mathf.Tau - 0.55f, 16, _ink, S(0.8f), antialiased: true);

    private void BrowStroke(Control canvas, float x, float y, float innerDrop)
    {
        float inner = x < 0.0f ? x + 2.4f : x - 2.4f;
        float outer = x < 0.0f ? x - 2.4f : x + 2.4f;
        Stroke(canvas, outer, y + 0.6f, inner, y + 0.6f - innerDrop, 0.9f);
    }

    private void SmileArc(Control canvas, float x, float y, float radius, float stroke) =>
        canvas.DrawArc(P(x, y + (radius * 0.55f)), S(radius),
            0.35f, Mathf.Pi - 0.35f, 24, _ink, S(stroke), antialiased: true);

    private void FrownArc(Control canvas, float x, float y, float radius, float stroke) =>
        canvas.DrawArc(P(x, y - (radius * 0.55f)), S(radius),
            Mathf.Pi + 0.5f, Mathf.Tau - 0.5f, 24, _ink, S(stroke), antialiased: true);

    private void OpenSmile(Control canvas, float x, float y, float radius)
    {
        const int Segments = 24;
        var points = new Vector2[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Pi * i / Segments;
            points[i] = P(x, y) + new Vector2(
                Mathf.Cos(angle) * S(radius), Mathf.Sin(angle) * S(radius));
        }

        canvas.DrawColoredPolygon(points, _ink);
    }

    private void Ring(Control canvas, float x, float y, float radius, float stroke) =>
        canvas.DrawCircle(P(x, y), S(radius), _ink, filled: false, width: S(stroke),
            antialiased: true);

    private void Stroke(Control canvas, float x1, float y1, float x2, float y2, float stroke) =>
        canvas.DrawLine(P(x1, y1), P(x2, y2), _ink, S(stroke), antialiased: true);

    private void FillEllipse(Control canvas, float x, float y, float rx, float ry, Color color)
    {
        canvas.DrawSetTransform(P(x, y), 0.0f, new Vector2(1.0f, ry / rx));
        canvas.DrawCircle(Vector2.Zero, S(rx), color, filled: true, antialiased: true);
        canvas.DrawSetTransform(Vector2.Zero, 0.0f, Vector2.One);
    }

    private static Vector2 P(float x, float y) => new(
        (FaceCompositor.TextureSize / 2.0f) + (x * Scale),
        (FaceCompositor.TextureSize / 2.0f) - (y * Scale));

    private static float S(float value) => value * Scale;
}

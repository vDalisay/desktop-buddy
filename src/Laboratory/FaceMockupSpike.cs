using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>
/// Throwaway face-art mockup spike for the M3.6 owner gate (DECISIONS.md 2026-07-18:
/// "the owner picks from 2-3 rendered face variants (spike-style minimal ink dots among
/// them) in a development-only preview before the composed face is implemented").
///
/// Development-only, export-excluded. Not wired into any shipping scene. Launch:
///   Godot_console.exe --path . scenes/spike_face_mockup.tscn
///
/// Presents a 3x4 grid of heads rendered with the ACCEPTED M3.5 look (soft-toon head
/// material, shared ink outline shell, two-light shadowless rig — all built from the same
/// lab_buddy_look.tres the presenter ships with):
///   rows    = art variants  A INK DOTS / B SOFT OVAL / C BEAN + BLUSH
///   columns = expressions   neutral :| / delight ^_^ / pain &gt;_&lt; / startle o_o
/// The rightmost column sits over a dark busy-desktop panel (contrast read). Faces are
/// drawn procedurally into SubViewport textures mounted on a head-front quad at
/// surface + epsilon — the exact mounting Task 5 ships — so what is judged here is what
/// the FaceCompositor will produce.
///
/// Keys:  Y cycles yaw 0 / +30 / -30 on every head (the accepted three-quarter states —
///        the M3.5 sideways-glyph hazard check);  Esc quits.
/// </summary>
public partial class FaceMockupSpike : Node3D
{
    private const int WindowWidth = 1024;
    private const int WindowHeight = 640;
    private const float CameraDistance = 500f;
    private const float CameraSize = 440f;

    // Head geometry mirrors the shipping presenter: rig head radius 24, face plate in
    // front of the sphere surface. The quad is flat while the sphere curves away, so
    // only the quad center needs the epsilon.
    private const float HeadRadius = 24f;
    private const float FaceQuadSize = 40f;
    private const float FaceDepthEpsilon = 0.4f;

    // PartHead albedo from lab_buddy_visual.tres (the accepted lab body colors).
    private static readonly Color HeadColor = new(0.478f, 0.78f, 1f);
    private static readonly Color BackdropNeutral = new(0.86f, 0.88f, 0.89f);
    private static readonly Color BackdropBusy = new(0.14f, 0.18f, 0.25f);
    private static readonly Color LabelLight = new(0.91f, 0.95f, 1f);

    private static readonly float[] ColumnX = { -180f, -60f, 60f, 180f };
    private static readonly float[] RowY = { 125f, 0f, -125f };
    private static readonly float[] YawCycleDegrees = { 0f, 30f, -30f };

    private readonly System.Collections.Generic.List<Node3D> _yawPivots = new();
    private int _yawIndex;
    private Label3D _helpLabel = null!;

    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(WindowWidth, WindowHeight);
        GetWindow().Title = "Face mockup gate — M3.6";

        var look = GD.Load<DesktopBuddy.Buddy.Presentation3D.BuddyLookProfile>(
            "res://data/buddy/lab_buddy_look.tres");
        var materials = new DesktopBuddy.Buddy.Presentation3D.BuddyLookMaterialLibrary(look);

        var lights = new DesktopBuddy.Buddy.Presentation3D.BuddyLookLightingRig();
        AddChild(lights);
        lights.Initialize(look);

        AddChild(new Camera3D
        {
            Name = "MockupCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            KeepAspect = Camera3D.KeepAspectEnum.Height,
            Size = CameraSize,
            Position = new Vector3(0f, 0f, CameraDistance),
            Current = true,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        });

        BuildBackdrop();
        BuildGrid(look, materials);
        BuildLabels();
    }

    private void BuildBackdrop()
    {
        AddPanel(new Vector2(1000f, 460f), new Vector3(-140f, 0f, -200f), BackdropNeutral);
        // Busy strip runs from left of the startle column to past the window edge.
        AddPanel(new Vector2(280f, 460f), new Vector3(255f, 0f, -199f), BackdropBusy);
        // Two clutter blocks (clear of every head) so the busy column reads as "over a
        // real desktop".
        AddPanel(new Vector2(44f, 28f), new Vector3(138f, 64f, -198f),
            new Color(0.93f, 0.31f, 0.32f), rotationZDegrees: 11f);
        AddPanel(new Vector2(30f, 78f), new Vector3(224f, -64f, -198f),
            new Color(0.98f, 0.72f, 0.18f), rotationZDegrees: -12f);
    }

    private void AddPanel(Vector2 size, Vector3 position, Color color, float rotationZDegrees = 0f)
    {
        AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = size },
            Position = position,
            RotationDegrees = new Vector3(0f, 0f, rotationZDegrees),
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = color,
            },
        });
    }

    private void BuildGrid(
        DesktopBuddy.Buddy.Presentation3D.BuddyLookProfile look,
        DesktopBuddy.Buddy.Presentation3D.BuddyLookMaterialLibrary materials)
    {
        var headMesh = new SphereMesh
        {
            Radius = HeadRadius,
            Height = HeadRadius * 2f,
            RadialSegments = 48,
            Rings = 24,
        };

        FaceVariant[] variants =
            { FaceVariant.InkDots, FaceVariant.SoftOval, FaceVariant.BeanBlush };
        FaceExpression[] expressions =
        {
            FaceExpression.Neutral, FaceExpression.Delight,
            FaceExpression.Pain, FaceExpression.Startle,
        };

        for (int row = 0; row < variants.Length; row++)
        {
            for (int column = 0; column < expressions.Length; column++)
            {
                BuildHead(
                    headMesh, materials, look,
                    new Vector3(ColumnX[column], RowY[row], 0f),
                    variants[row], expressions[column]);
            }
        }
    }

    private void BuildHead(
        SphereMesh headMesh,
        DesktopBuddy.Buddy.Presentation3D.BuddyLookMaterialLibrary materials,
        DesktopBuddy.Buddy.Presentation3D.BuddyLookProfile look,
        Vector3 position,
        FaceVariant variant,
        FaceExpression expression)
    {
        var pivot = new Node3D
        {
            Name = $"Head_{variant}_{expression}",
            Position = position,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(pivot);
        _yawPivots.Add(pivot);

        // Inverted-hull outline shell + lit head, exactly the presenter's construction.
        pivot.AddChild(new MeshInstance3D
        {
            Name = "Outline",
            Mesh = headMesh,
            MaterialOverride = materials.OutlineMaterial,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        });
        pivot.AddChild(new MeshInstance3D
        {
            Name = "Head",
            Mesh = headMesh,
            MaterialOverride = materials.CreateLitMaterial(HeadColor),
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        });

        // Face texture: procedural CanvasItem draw inside a SubViewport, mounted on a
        // head-front quad — the Task 5 FaceCompositor mounting. Always-update is spike
        // laxity (twelve static 200x200 targets); the shipping compositor re-renders on
        // change only.
        var viewport = new SubViewport
        {
            Size = new Vector2I(FaceMockupCell.TextureSize, FaceMockupCell.TextureSize),
            TransparentBg = true,
            Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        viewport.AddChild(new FaceMockupCell
        {
            Variant = variant,
            Expression = expression,
            Ink = look.OutlineColor,
            Size = new Vector2(FaceMockupCell.TextureSize, FaceMockupCell.TextureSize),
        });
        pivot.AddChild(viewport);

        pivot.AddChild(new MeshInstance3D
        {
            Name = "FacePlate",
            Mesh = new QuadMesh { Size = new Vector2(FaceQuadSize, FaceQuadSize) },
            Position = new Vector3(0f, 0f, HeadRadius + FaceDepthEpsilon),
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoTexture = viewport.GetTexture(),
            },
        });
    }

    private void BuildLabels()
    {
        string[] rowNames = { "A  INK DOTS", "B  SOFT OVAL", "C  BEAN + BLUSH" };
        for (int row = 0; row < rowNames.Length; row++)
        {
            AddLabel(rowNames[row], new Vector3(-280f, RowY[row], 0f), BackdropBusy);
        }

        string[] columnNames = { "NEUTRAL  :|", "DELIGHT  ^_^", "PAIN  >_<", "STARTLE  o_o" };
        for (int column = 0; column < columnNames.Length; column++)
        {
            AddLabel(columnNames[column], new Vector3(ColumnX[column], 185f, 0f),
                column == ColumnX.Length - 1 ? LabelLight : BackdropBusy);
        }

        _helpLabel = AddLabel(
            "Y : yaw 0 / +30 / -30      Esc : quit",
            new Vector3(0f, -195f, 0f), BackdropBusy);
    }

    private Label3D AddLabel(string text, Vector3 position, Color color)
    {
        var label = new Label3D
        {
            Text = text,
            Position = position,
            PixelSize = 1f,
            FontSize = 12,
            OutlineSize = 0,
            Modulate = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off,
        };
        AddChild(label);
        return label;
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        switch (key.Keycode)
        {
            case Key.Y:
                _yawIndex = (_yawIndex + 1) % YawCycleDegrees.Length;
                float yaw = YawCycleDegrees[_yawIndex];
                foreach (Node3D pivot in _yawPivots)
                {
                    pivot.RotationDegrees = new Vector3(0f, yaw, 0f);
                }

                _helpLabel.Text =
                    $"Y : yaw 0 / +30 / -30   (now {yaw:+0;-0;0}°)      Esc : quit";
                break;
            case Key.Escape:
                GetTree().Quit();
                break;
        }
    }
}

public enum FaceVariant
{
    /// <summary>Spike-minimal: filled ink dot eyes, thin line mouth. The DECISIONS-required baseline.</summary>
    InkDots,

    /// <summary>Soft vertical oval eyes with a white highlight, subtle brows, rounded mouths.</summary>
    SoftOval,

    /// <summary>Large ink bean eyes with big sparkle highlights, cheek blush, expressive mouths.</summary>
    BeanBlush,
}

public enum FaceExpression
{
    Neutral,
    Delight,
    Pain,
    Startle,
}

/// <summary>
/// Draws one face variant/expression combination with CanvasItem primitives. Face
/// coordinates are world pixels on the 40x40 face plate (x right, y up, origin at head
/// center), scaled 5x into the 200x200 render target.
/// </summary>
public partial class FaceMockupCell : Control
{
    public const int TextureSize = 200;
    private const float PixelsPerUnit = TextureSize / FaceMockupSpikeConstants.FacePlateSize;

    public FaceVariant Variant { get; set; }
    public FaceExpression Expression { get; set; }
    public Color Ink { get; set; }

    private static readonly Color White = new(1f, 1f, 1f);
    private static readonly Color Blush = new(0.95f, 0.55f, 0.6f, 0.42f);

    public override void _Draw()
    {
        switch (Variant)
        {
            case FaceVariant.InkDots:
                DrawInkDots();
                break;
            case FaceVariant.SoftOval:
                DrawSoftOval();
                break;
            case FaceVariant.BeanBlush:
                DrawBeanBlush();
                break;
        }
    }

    // --- variant painters ------------------------------------------------------------

    private void DrawInkDots()
    {
        switch (Expression)
        {
            case FaceExpression.Neutral:
                DotEye(-8f, 4f);
                DotEye(8f, 4f);
                Stroke(-3.5f, -7f, 3.5f, -7f, 1.6f);
                break;
            case FaceExpression.Delight:
                HappyEyeArc(-8f, 3f, 3.4f, 1.7f);
                HappyEyeArc(8f, 3f, 3.4f, 1.7f);
                SmileArc(0f, -4.5f, 4.5f, 1.7f);
                break;
            case FaceExpression.Pain:
                ScrunchEye(-8f, 4f, 1.7f);
                ScrunchEye(8f, 4f, 1.7f);
                FrownArc(0f, -8f, 4f, 2f);
                break;
            case FaceExpression.Startle:
                RingEye(-8f, 4f, 3.2f, 1.5f);
                RingEye(8f, 4f, 3.2f, 1.5f);
                RingMouth(0f, -7f, 1.8f, 1.4f);
                break;
        }
    }

    private void DrawSoftOval()
    {
        switch (Expression)
        {
            case FaceExpression.Neutral:
                BrowArc(-8f, 9.5f, 0f);
                BrowArc(8f, 9.5f, 0f);
                OvalEye(-8f, 3.5f, 2.6f, 4f);
                OvalEye(8f, 3.5f, 2.6f, 4f);
                Stroke(-2.5f, -7f, 2.5f, -7f, 2f);
                break;
            case FaceExpression.Delight:
                BrowArc(-8f, 10.5f, 0f);
                BrowArc(8f, 10.5f, 0f);
                HappyEyeArc(-8f, 3.5f, 3.6f, 2.2f);
                HappyEyeArc(8f, 3.5f, 3.6f, 2.2f);
                OpenSmile(0f, -5f, 4f);
                break;
            case FaceExpression.Pain:
                BrowStroke(-8f, 9f, tiltInward: true);
                BrowStroke(8f, 9f, tiltInward: true);
                ScrunchEye(-8f, 3.5f, 2.2f);
                ScrunchEye(8f, 3.5f, 2.2f);
                FrownArc(0f, -8.5f, 4f, 2f);
                break;
            case FaceExpression.Startle:
                BrowArc(-8f, 11.5f, 0f);
                BrowArc(8f, 11.5f, 0f);
                PupilEye(-8f, 3.5f, 3.6f, 1.4f);
                PupilEye(8f, 3.5f, 3.6f, 1.4f);
                RingMouth(0f, -7f, 2f, 1.6f);
                break;
        }
    }

    private void DrawBeanBlush()
    {
        bool pain = Expression == FaceExpression.Pain;
        BlushOval(-11f, -3.5f, pain);
        BlushOval(11f, -3.5f, pain);

        switch (Expression)
        {
            case FaceExpression.Neutral:
                BeanEye(-7.5f, 3f, 3.2f, 5f, highlight: 1.5f);
                BeanEye(7.5f, 3f, 3.2f, 5f, highlight: 1.5f);
                SmileArc(0f, -8.5f, 2.8f, 1.8f);
                break;
            case FaceExpression.Delight:
                HappyEyeArc(-7.5f, 3.5f, 3.8f, 2.4f);
                HappyEyeArc(7.5f, 3.5f, 3.8f, 2.4f);
                OpenSmile(0f, -4.5f, 4.5f);
                break;
            case FaceExpression.Pain:
                ScrunchEye(-7.5f, 3f, 2.4f);
                ScrunchEye(7.5f, 3f, 2.4f);
                Squiggle(0f, -7f, 1.8f);
                break;
            case FaceExpression.Startle:
                BeanEye(-7.5f, 3f, 3.4f, 5.6f, highlight: 0.8f);
                BeanEye(7.5f, 3f, 3.4f, 5.6f, highlight: 0.8f);
                RingMouth(0f, -7.5f, 1.6f, 1.4f);
                break;
        }
    }

    // --- feature pieces (face coordinates: x right, y up, world px) ------------------

    private void DotEye(float x, float y) =>
        DrawCircle(P(x, y), S(2.2f), Ink, filled: true, antialiased: true);

    private void OvalEye(float x, float y, float rx, float ry)
    {
        FillEllipse(x, y, rx, ry, Ink);
        DrawCircle(P(x - 0.8f, y + 1.3f), S(0.9f), White, filled: true, antialiased: true);
    }

    private void BeanEye(float x, float y, float rx, float ry, float highlight)
    {
        float tilt = x < 0f ? 8f : -8f;
        FillEllipse(x, y, rx, ry, Ink, tilt);
        DrawCircle(P(x - Mathf.Sign(x) * 1.1f, y + 1.8f), S(highlight),
            White, filled: true, antialiased: true);
        DrawCircle(P(x + Mathf.Sign(x) * 0.8f, y - 1.6f), S(highlight * 0.45f),
            White, filled: true, antialiased: true);
    }

    private void PupilEye(float x, float y, float radius, float pupil)
    {
        DrawCircle(P(x, y), S(radius), White, filled: true, antialiased: true);
        DrawCircle(P(x, y), S(radius), Ink, filled: false, width: S(0.5f), antialiased: true);
        DrawCircle(P(x, y), S(pupil), Ink, filled: true, antialiased: true);
    }

    private void RingEye(float x, float y, float radius, float stroke) =>
        DrawCircle(P(x, y), S(radius), Ink, filled: false, width: S(stroke),
            antialiased: true);

    /// <summary>Closed happy eye: an upward bow (top-half arc).</summary>
    private void HappyEyeArc(float x, float y, float radius, float stroke) =>
        DrawArc(P(x, y - radius * 0.4f), S(radius),
            Mathf.Pi + 0.35f, Mathf.Tau - 0.35f, 24, Ink, S(stroke), antialiased: true);

    /// <summary>Pain eye: a "&gt;" (left) or "&lt;" (right) scrunch.</summary>
    private void ScrunchEye(float x, float y, float stroke)
    {
        float tip = x < 0f ? x + 2.2f : x - 2.2f;
        float back = x < 0f ? x - 2.6f : x + 2.6f;
        Stroke(back, y + 2.6f, tip, y, stroke);
        Stroke(back, y - 2.6f, tip, y, stroke);
    }

    private void BrowArc(float x, float y, float raise) =>
        DrawArc(P(x, y + raise - 1.2f), S(3f),
            Mathf.Pi + 0.55f, Mathf.Tau - 0.55f, 16, Ink, S(0.8f), antialiased: true);

    private void BrowStroke(float x, float y, bool tiltInward)
    {
        float inner = x < 0f ? x + 2.4f : x - 2.4f;
        float outer = x < 0f ? x - 2.4f : x + 2.4f;
        float innerY = tiltInward ? y - 1.4f : y;
        Stroke(outer, y + 0.6f, inner, innerY, 0.7f);
    }

    /// <summary>Smile: a downward bow (bottom-half arc), face-up in world coordinates.</summary>
    private void SmileArc(float x, float y, float radius, float stroke) =>
        DrawArc(P(x, y + radius * 0.55f), S(radius),
            0.35f, Mathf.Pi - 0.35f, 24, Ink, S(stroke), antialiased: true);

    private void FrownArc(float x, float y, float radius, float stroke) =>
        DrawArc(P(x, y - radius * 0.55f), S(radius),
            Mathf.Pi + 0.5f, Mathf.Tau - 0.5f, 24, Ink, S(stroke), antialiased: true);

    /// <summary>Open smile: filled semicircle, flat side up.</summary>
    private void OpenSmile(float x, float y, float radius)
    {
        const int Segments = 24;
        var points = new Vector2[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Pi * i / Segments;
            points[i] = P(x, y) + new Vector2(
                Mathf.Cos(angle) * S(radius), Mathf.Sin(angle) * S(radius));
        }

        DrawColoredPolygon(points, Ink);
    }

    private void RingMouth(float x, float y, float radius, float stroke) =>
        DrawCircle(P(x, y), S(radius), Ink, filled: false, width: S(stroke),
            antialiased: true);

    private void Squiggle(float x, float y, float stroke)
    {
        Stroke(x - 3f, y, x - 1f, y + 1.2f, stroke);
        Stroke(x - 1f, y + 1.2f, x + 1f, y - 1.2f, stroke);
        Stroke(x + 1f, y - 1.2f, x + 3f, y, stroke);
    }

    private void BlushOval(float x, float y, bool strong) =>
        FillEllipse(x, y, 2.8f, 1.6f, strong ? Blush with { A = 0.6f } : Blush);

    // --- primitives ------------------------------------------------------------------

    private void Stroke(float x1, float y1, float x2, float y2, float stroke) =>
        DrawLine(P(x1, y1), P(x2, y2), Ink, S(stroke), antialiased: true);

    private void FillEllipse(float x, float y, float rx, float ry, Color color,
        float rotationDegrees = 0f)
    {
        DrawSetTransform(P(x, y), Mathf.DegToRad(rotationDegrees), new Vector2(1f, ry / rx));
        DrawCircle(Vector2.Zero, S(rx), color, filled: true, antialiased: true);
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private static Vector2 P(float x, float y) =>
        new(TextureSize / 2f + x * PixelsPerUnit, TextureSize / 2f - y * PixelsPerUnit);

    private static float S(float value) => value * PixelsPerUnit;
}

internal static class FaceMockupSpikeConstants
{
    /// <summary>World size of the face plate quad; keep in sync with FaceMockupSpike.</summary>
    public const float FacePlateSize = 40f;
}

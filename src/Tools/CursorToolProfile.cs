using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Provisional, laboratory-tunable tuning for one cursor-tethered physical tool
/// (RAGDOLL §9.1/§9.2). Every such tool reuses the M1 damped-elastic tether
/// mechanism and differs only in authored data: which tool it serves, its shape
/// and mass, its tether gains, and — for an elongated tool — how firmly it holds
/// square to its own swing. Pain always comes from the real measured contact
/// impulse through the shared pain curve; there is no per-tool payout multiplier.
///
/// <see cref="ContentId"/> is the authored stable ID (ARCHITECTURE §5) rather
/// than an exported enum, so attribution, harmful-history memory, and statistics
/// all key on the same vocabulary the catalogue and save payloads use.
/// </summary>
[GlobalClass]
public partial class CursorToolProfile : GameResource
{
    [Export] public string ContentId { get; set; } = ContentIds.ToolBoxingGlove;

    /// <summary>
    /// Half-width of the collider. For an elongated tool this is the barrel's
    /// half-thickness and <see cref="Length"/> supplies the rest.
    /// </summary>
    [Export(PropertyHint.Range, "1,128,0.1,or_greater")] public float Radius { get; set; } = 14.0f;

    /// <summary>
    /// Total length of an elongated collider along its own long axis, or <c>0</c>
    /// for a plain circle. A capsule's long axis is its local Y, which is what the
    /// alignment servo steers.
    /// </summary>
    [Export(PropertyHint.Range, "0,512,0.1,or_greater")] public float Length { get; set; }

    [Export(PropertyHint.Range, "0.01,100,0.01,or_greater")] public float Mass { get; set; } = 3.0f;
    [Export(PropertyHint.Range, "0,100000,0.1,or_greater")] public float Stiffness { get; set; } = 900.0f;
    [Export(PropertyHint.Range, "0,10000,0.1,or_greater")] public float Damping { get; set; } = 45.0f;
    [Export(PropertyHint.Range, "0.1,200000,0.1,or_greater")] public float MaximumForce { get; set; } = 30_000.0f;
    [Export(PropertyHint.Range, "0,100,0.01,or_greater")] public float LinearDamp { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,100,0.01,or_greater")] public float AngularDamp { get; set; } = 1.0f;

    /// <summary>
    /// Alignment servo gains. A stiffness of <c>0</c> disables alignment outright,
    /// which is how a circular tool authors "never steer my rotation" without the
    /// controller branching on shape.
    /// </summary>
    [Export(PropertyHint.Range, "0,1000000,1,or_greater")] public float AlignStiffness { get; set; }

    [Export(PropertyHint.Range, "0,100000,1,or_greater")] public float AlignDamping { get; set; }

    [Export(PropertyHint.Range, "0.1,1000000,1,or_greater")] public float MaximumAlignTorque { get; set; } = 100_000.0f;

    /// <summary>
    /// Cursor speed below which a swing has no direction worth aligning to, so the
    /// tool holds the angle it already had instead of snapping to solver noise.
    /// </summary>
    [Export(PropertyHint.Range, "0,4000,1,or_greater")] public float MinimumAlignSpeed { get; set; } = 60.0f;

    [Export(PropertyHint.Range, "0.1,128,0.1,or_greater")] public float MinimumArmingTravel { get; set; } = 8.0f;
    [Export(PropertyHint.Range, "0,32,0.1")] public float WallClearance { get; set; } = 2.0f;
    [Export] public Color VisualColor { get; set; } = new("e05b4b");
    [Export] public Color OutlineColor { get; set; } = new("5c1a1a");
    [Export] public float VisualDepthOffset { get; set; } = 144.0f;

    /// <summary>
    /// Grip/charge/swing handling, or <c>null</c> for a tool that is only ever
    /// dragged by the cursor. The Boxing Glove authors none, which is how it
    /// keeps its exact behavior without anything branching on a tool name.
    /// </summary>
    [Export] public SwingToolProfile? Swing { get; set; }

    /// <summary>True when this tool's collider is elongated rather than circular.</summary>
    public bool IsElongated => Length > 0.0f;

    /// <summary>True when this tool can be gripped, charged, and swung.</summary>
    public bool IsSwingCapable =>
        Swing is not null && GodotObject.IsInstanceValid(Swing) && IsElongated;

    /// <summary>
    /// The grip point in body-local coordinates: the centre of the capsule's
    /// handle-end hemisphere. Derived from the collider and never authored — a
    /// grip point that could disagree with the shape it grips is a data trap,
    /// not a tuning affordance.
    ///
    /// A <see cref="CapsuleShape2D"/> runs along its own local Y, and this build
    /// treats local <c>+Y</c> as the handle end, so the barrel is local <c>-Y</c>
    /// and points up when the body's rotation is zero.
    /// </summary>
    public Vector2 HandleLocalOffset => new(0.0f, (Length * 0.5f) - Radius);

    /// <summary>Handle-to-tip lever arm — the radius the barrel tip sweeps about the grip.</summary>
    public float HandleToTipRadius => Length - Radius;

    /// <summary>
    /// Handle-to-centre-of-mass distance. This is the radius in the centripetal
    /// load a handle pivot has to survive, which is a different (and much
    /// smaller) number than the tip's lever arm.
    /// </summary>
    public float HandleToCenterOfMassRadius => (Length * 0.5f) - Radius;

    /// <summary>
    /// The tool this profile serves. Only valid once <see cref="Validate"/> passes;
    /// callers reach it through the controller, which validates on initialization.
    /// </summary>
    public ToolId Tool
    {
        get
        {
            ContentIds.TryParseTool(ContentId, out ToolId tool);
            return tool;
        }
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (!ContentIds.TryParseTool(ContentId, out _))
        {
            errors.Add(
                $"{nameof(ContentId)} must name a tool known to this build, not '{ContentId}'");
        }

        if (!float.IsFinite(Radius) || Radius <= 0.0f)
        {
            errors.Add($"{nameof(Radius)} must be finite and positive");
        }

        if (!float.IsFinite(Length) || Length < 0.0f)
        {
            errors.Add($"{nameof(Length)} must be finite and non-negative");
        }

        // A capsule shorter than it is thick is a circle with extra steps, and Godot
        // silently clamps it — authoring one is a data mistake, not a shape choice.
        if (IsElongated && Length <= Radius * 2.0f)
        {
            errors.Add($"{nameof(Length)} must exceed the collider's full width when elongated");
        }

        if (!float.IsFinite(Mass) || Mass <= 0.0f)
        {
            errors.Add($"{nameof(Mass)} must be finite and positive");
        }

        if (!float.IsFinite(Stiffness) || Stiffness < 0.0f)
        {
            errors.Add($"{nameof(Stiffness)} must be finite and non-negative");
        }

        if (!float.IsFinite(Damping) || Damping < 0.0f)
        {
            errors.Add($"{nameof(Damping)} must be finite and non-negative");
        }

        if (!float.IsFinite(MaximumForce) || MaximumForce <= 0.0f)
        {
            errors.Add($"{nameof(MaximumForce)} must be finite and positive");
        }

        if (!float.IsFinite(LinearDamp) || LinearDamp < 0.0f)
        {
            errors.Add($"{nameof(LinearDamp)} must be finite and non-negative");
        }

        if (!float.IsFinite(AngularDamp) || AngularDamp < 0.0f)
        {
            errors.Add($"{nameof(AngularDamp)} must be finite and non-negative");
        }

        if (!float.IsFinite(AlignStiffness) || AlignStiffness < 0.0f)
        {
            errors.Add($"{nameof(AlignStiffness)} must be finite and non-negative");
        }

        if (!float.IsFinite(AlignDamping) || AlignDamping < 0.0f)
        {
            errors.Add($"{nameof(AlignDamping)} must be finite and non-negative");
        }

        if (!float.IsFinite(MaximumAlignTorque) || MaximumAlignTorque <= 0.0f)
        {
            errors.Add($"{nameof(MaximumAlignTorque)} must be finite and positive");
        }

        if (!float.IsFinite(MinimumAlignSpeed) || MinimumAlignSpeed < 0.0f)
        {
            errors.Add($"{nameof(MinimumAlignSpeed)} must be finite and non-negative");
        }

        // An unsteered elongated collider tumbles off every contact and reads as a
        // floating stick rather than a swung tool, so the pairing is required data.
        if (IsElongated && AlignStiffness <= 0.0f)
        {
            errors.Add($"{nameof(AlignStiffness)} must be positive for an elongated tool");
        }

        if (!float.IsFinite(MinimumArmingTravel) || MinimumArmingTravel <= 0.0f)
        {
            errors.Add($"{nameof(MinimumArmingTravel)} must be finite and positive");
        }

        if (!float.IsFinite(WallClearance) || WallClearance < 0.0f)
        {
            errors.Add($"{nameof(WallClearance)} must be finite and non-negative");
        }

        if (!float.IsFinite(VisualDepthOffset))
        {
            errors.Add($"{nameof(VisualDepthOffset)} must be finite");
        }

        if (Swing is not null && GodotObject.IsInstanceValid(Swing))
        {
            foreach (string error in Swing.Validate())
            {
                errors.Add($"{nameof(Swing)}: {error}");
            }

            // The arc's feasibility depends on this collider's shape and mass, so
            // the cross-checks live here where both halves are in hand.
            Swing.ValidateAgainstCollider(errors, this);
        }

        return errors;
    }
}

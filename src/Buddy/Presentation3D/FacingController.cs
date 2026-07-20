using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// M3.6 Task 2 facing controller: owns the buddy's committed three-quarter side and the
/// eased BodyYaw value the presenter applies (scaled by the pose pipeline's performance
/// weight, so Tracking cuts snap the displayed yaw to zero while the committed side is
/// remembered). Arbitration and easing live engine-free in <see cref="FacingModel"/>;
/// this node only samples real semantics — the engaged care/glove cursor side, the
/// arbitrated drive walk direction — and re-derives its seeded idle-variety stream from
/// every autonomy reseed so laboratory runs stay deterministic per seed. Presentation
/// only: never writes gameplay state.
/// </summary>
[GlobalClass]
public partial class FacingController : Node
{
    // Distinct stream per consumer family (IRandomSource contract): facing variety must
    // never perturb autonomy outcomes, so it salts the shared seed into its own stream.
    private const ulong FacingStreamSalt = 0xFACE_5EED_2026_0718UL;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public InteractionDamageComponent DamagePipeline { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public BoxingGloveController Glove { get; set; } = null!;
    [Export] public BuddyExpressionProfile Profile { get; set; } = null!;

    private FacingModel _model = null!;
    private long _lastRoutedTick;

    public bool IsInitialized { get; private set; }
    public FacingSide CommittedSide => IsInitialized ? _model.CommittedSide : FacingSide.Frontal;
    public float CurrentYawDegrees => IsInitialized ? _model.CurrentYawDegrees : 0.0f;

    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(DamagePipeline) || !DamagePipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(CareStroke) ||
            !GodotObject.IsInstanceValid(Glove) ||
            !GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException("FacingController dependencies are incomplete.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy expression profile: {string.Join("; ", errors)}");
        }

        Reseed(Buddy.AutonomousMotion.Seed);
        Buddy.AutonomyReseeded += Reseed;
        _lastRoutedTick = Buddy.RoutedTicks;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (IsInitialized && GodotObject.IsInstanceValid(Buddy))
        {
            Buddy.AutonomyReseeded -= Reseed;
        }
    }

    /// <summary>Rebuilds the facing stream from the shared seed (own salted stream).</summary>
    public void Reseed(ulong seed) => _model = new FacingModel(
        new SeededRandomSource(seed ^ FacingStreamSalt),
        Profile.ToData().ToFacingParameters());

    /// <summary>
    /// Samples current semantics and advances the model; returns the eased yaw in
    /// degrees. Called by the presenter once per rendered frame; allocation-free.
    /// </summary>
    public float Evaluate(double deltaSeconds)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("FacingController used before initialization.");
        }

        // The simulation's routed clock, not the engine frame counter: a paused lab must
        // not accumulate hysteresis or fire idle-variety flips behind a frozen buddy.
        long now = Buddy.RoutedTicks;
        int ticksElapsed = (int)Math.Clamp(now - _lastRoutedTick, 0, int.MaxValue);
        _lastRoutedTick = now;

        bool engaged = false;
        float side = 0.0f;
        float torsoX = Buddy.Rig.Torso.GlobalPosition.X;
        ToolId tool = DamagePipeline.SelectedTool;
        if ((tool == ToolId.Pet || tool == ToolId.Tickle) &&
            CareStroke.IsHeld && CareStroke.LastContactValid)
        {
            engaged = true;
            side = MathF.Sign(CareStroke.Cursor.X - torsoX);
        }
        else if (tool == ToolId.BoxingGlove && Glove.HasCursor)
        {
            engaged = true;
            side = MathF.Sign(Glove.Cursor.X - torsoX);
        }

        var inputs = new FacingInputs(engaged, side, Buddy.CurrentDriveIntent.WalkDirection);
        return _model.Update(inputs, ticksElapsed, deltaSeconds);
    }
}

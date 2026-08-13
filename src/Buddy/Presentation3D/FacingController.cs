using System;
using DesktopBuddy.Diagnostics;
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

    /// <summary>Matches the look-at release margin, so gaze and facing let go together.</summary>
    private const float CursorReleaseRangeFactor = 1.25f;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public InteractionDamageComponent DamagePipeline { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public CursorToolController CursorTools { get; set; } = null!;
    [Export] public BuddyExpressionProfile Profile { get; set; } = null!;

    private FacingModel _model = null!;
    private long _lastRoutedTick;
    private int _developmentSide;
    private bool _cursorEngaged;
    private bool _tracedTurnActive;
    private float _tracedTargetYaw;
    private string _tracedTurnSource = "rest";

    public bool IsInitialized { get; private set; }
    public FacingSide CommittedSide => IsInitialized ? _model.CommittedSide : FacingSide.Frontal;
    public float CurrentYawDegrees => IsInitialized ? _model.CurrentYawDegrees : 0.0f;

    /// <summary>-1/+1 while a development override stands in for an engaged cursor, else 0.</summary>
    public int DevelopmentSide => _developmentSide;

    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(DamagePipeline) || !DamagePipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(CareStroke) ||
            !GodotObject.IsInstanceValid(CursorTools) ||
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
        if (BuildInfo.IsDebugBuild && _tracedTurnActive)
            TraceTurn("end", "node_exit", CurrentYawDegrees, _tracedTargetYaw, _tracedTurnSource);

        if (IsInitialized && GodotObject.IsInstanceValid(Buddy))
        {
            Buddy.AutonomyReseeded -= Reseed;
        }
    }

    /// <summary>Rebuilds the facing stream from the shared seed (own salted stream).</summary>
    public void Reseed(ulong seed)
    {
        if (BuildInfo.IsDebugBuild && _tracedTurnActive && _model is not null)
            TraceTurn("end", "reseeded", CurrentYawDegrees, _tracedTargetYaw, _tracedTurnSource);

        _model = new FacingModel(
            new SeededRandomSource(seed ^ FacingStreamSalt),
            Profile.ToData().ToFacingParameters());
        _tracedTurnActive = false;
        _tracedTargetYaw = 0.0f;
        _tracedTurnSource = "rest";
    }

    /// <summary>
    /// Development-only drive (laboratory keys, debug builds): stands in for an engaged
    /// cursor on the given side so a turn can be triggered without a tool. Pass 0 to hand
    /// arbitration back to the real inputs; the model itself is untouched, so easing,
    /// hysteresis, and priority stay exactly the shipping ones.
    /// </summary>
    public void SetDevelopmentSide(int side)
    {
        if (!BuildInfo.IsDebugBuild)
        {
            return;
        }

        _developmentSide = Math.Sign(side);
    }

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
        Vector2 torso = Buddy.Rig.Torso.GlobalPosition;
        float torsoX = torso.X;
        ToolId tool = DamagePipeline.SelectedTool;
        if (_developmentSide != 0)
        {
            engaged = true;
            side = _developmentSide;
        }
        else if ((tool == ToolId.Pet || tool == ToolId.Tickle) &&
            CareStroke.IsHeld && CareStroke.LastContactValid)
        {
            engaged = true;
            side = MathF.Sign(CareStroke.Cursor.X - torsoX);
        }
        else if (CursorToolWithinReach(tool, torso))
        {
            engaged = true;
            side = MathF.Sign(CursorTools.Cursor.X - torsoX);
        }
        else if (Buddy.ObjectInteraction.IsHolding)
        {
            // Holding something is an engagement with the player: the buddy turns to face them
            // while it carries the ball, which is also the side it is about to throw toward
            // (owner instruction 2026-07-27). Ranked below the live tools, because a hand
            // actually on the buddy is the more immediate attention.
            engaged = true;
            side = MathF.Sign(Buddy.ObjectInteraction.CursorWorldPosition.X - torsoX);
        }

        // Eating faces front, and so does refusing: the point of the head-shake is that it is
        // aimed at the player who offered the food (owner instruction 2026-07-29).
        bool facesFront = Buddy.Activity.Current is ActivityId.Eat or ActivityId.Refuse;
        var inputs = new FacingInputs(
            engaged,
            side,
            Buddy.CurrentDriveIntent.WalkDirection,
            ForceFrontal: facesFront);
        float yaw = _model.Update(inputs, ticksElapsed, deltaSeconds);
        TraceFacingTurn(inputs, yaw);
        return yaw;
    }

    private void TraceFacingTurn(in FacingInputs inputs, float yaw)
    {
        if (!BuildInfo.IsDebugBuild)
            return;

        float target = inputs.ForceFrontal
            ? 0.0f
            : _model.CommittedSide switch
            {
                FacingSide.Left => -Profile.FacingYawDegrees,
                FacingSide.Right => Profile.FacingYawDegrees,
                _ => 0.0f,
            };
        string source = inputs.ForceFrontal ? "activity_front" :
            inputs.InteractionEngaged ? "cursor_or_object" :
            MathF.Abs(inputs.WalkDirection) > Profile.FacingWalkDeadband ? "walk" : "idle";

        if (!Mathf.IsEqualApprox(target, _tracedTargetYaw))
        {
            if (_tracedTurnActive)
                TraceTurn("end", "interrupted", yaw, _tracedTargetYaw, _tracedTurnSource);

            _tracedTargetYaw = target;
            _tracedTurnSource = source;
            _tracedTurnActive = !Mathf.IsEqualApprox(yaw, target);
            if (_tracedTurnActive)
                TraceTurn("start", "target_changed", yaw, target, source);
        }

        if (_tracedTurnActive && MathF.Abs(yaw - _tracedTargetYaw) < 0.05f)
        {
            TraceTurn("end", "completed", yaw, _tracedTargetYaw, _tracedTurnSource);
            _tracedTurnActive = false;
        }
    }

    private void TraceTurn(
        string @event, string reason, float yaw, float target, string source) =>
        Log.Debug("AnimationTrace",
            $"event={@event} lane=body.facing name=turn reason={reason} " +
            $"tick={Buddy.RoutedTicks} frame={Engine.GetProcessFrames()} source={source} " +
            $"yaw={yaw:0.###} target={target:0.###} side={CommittedSide} " +
            $"walk_direction={Buddy.CurrentDriveIntent.WalkDirection:0.###}");

    /// <summary>
    /// Whether a live cursor tool is close enough to be an engagement. Merely having a tool
    /// selected used to count, with no distance gate at all, so a pointer resting anywhere in
    /// the window pinned the committed side for as long as it sat there: the buddy walked one
    /// way while its body and head stayed turned the other (measured 2376/2400 frames pinned,
    /// 254 of them walking the opposite direction — owner report 2026-08-13). Out of reach the
    /// walk direction owns facing again, which is the "looking where it is going" default.
    /// The same acquire/release margin as <see cref="Domain.Presentation.LookAtModel"/> keeps a
    /// buddy walking along the range boundary from toggling between the two paths.
    /// </summary>
    private bool CursorToolWithinReach(ToolId tool, Vector2 torso)
    {
        if (!CursorTools.DrivesTool(tool) || !CursorTools.HasCursor)
        {
            _cursorEngaged = false;
            return false;
        }

        float range = Profile.LookEngagementRangePixels *
            (_cursorEngaged ? CursorReleaseRangeFactor : 1.0f);
        _cursorEngaged = CursorTools.Cursor.DistanceSquaredTo(torso) <= range * range;
        return _cursorEngaged;
    }
}

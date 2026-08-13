using System;
using DesktopBuddy.Buddy.Presentation;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// M3.6 Task 4 head look-at: owns what the buddy is watching and the eased head yaw/pitch
/// the presenter applies to the HEAD socket only (normally scaled by the pose pipeline's
/// performance weight; the presenter owns the explicit defend-gaze exception while the
/// body remains in Tracking). Arbitration, cone clamping, easing, and
/// the seeded ambient glance timer live engine-free in <see cref="LookAtModel"/>; this
/// node only samples real semantics — the engaged care/glove cursor, an eaten item in the
/// hand socket, the last accepted impact point, the current reaction face — and re-derives
/// its glance stream from every autonomy reseed on its OWN salted stream so laboratory
/// runs stay deterministic per seed without perturbing facing or autonomy.
///
/// Rotation-only and presentation-only: it writes nothing, not even a socket. The
/// presenter composes the angles (see <c>BuddyVisualPresenter.ApplyPartTransform</c>),
/// because the presenter overwrites every socket's rotation each frame.
/// </summary>
[GlobalClass]
public partial class HeadLookAtComponent : Node
{
    // Distinct stream per consumer family (IRandomSource contract): ambient glances must
    // perturb neither autonomy nor the facing controller's idle variety.
    private const ulong LookAtStreamSalt = 0x10_0CA7_2026_0720UL;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public InteractionDamageComponent DamagePipeline { get; set; } = null!;
    [Export] public CareStrokeComponent CareStroke { get; set; } = null!;
    [Export] public CursorToolController CursorTools { get; set; } = null!;
    [Export] public ActivityAnimator Activities { get; set; } = null!;
    [Export] public BuddyReactionComponent Reactions { get; set; } = null!;
    [Export] public BuddyExpressionProfile Profile { get; set; } = null!;

    private LookAtModel _model = null!;
    private long _lastRoutedTick;
    // Routed-tick stamp and world point of the last accepted impact; MinValue = never.
    private long _lastImpactTick = long.MinValue;
    private Vector2 _lastImpactPoint;
    private LookAtSource _tracedSource = LookAtSource.Rest;

    public bool IsInitialized { get; private set; }
    public LookAtSource CurrentSource => IsInitialized ? _model.CurrentSource : LookAtSource.Rest;
    public float CurrentYawDegrees => IsInitialized ? _model.CurrentYawDegrees : 0.0f;
    public float CurrentPitchDegrees => IsInitialized ? _model.CurrentPitchDegrees : 0.0f;

    /// <summary>
    /// Quantized pupil offset in [-1, 1] per axis — the applied angles normalized by the
    /// cone limits and snapped to the profile step count. Task 5's face compositor
    /// consumes this; the Task 4 scenario asserts it directly, with no face involved.
    /// </summary>
    public Vector2 PupilOffset => IsInitialized
        ? new Vector2(_model.PupilOffsetX, _model.PupilOffsetY)
        : Vector2.Zero;

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
            !GodotObject.IsInstanceValid(Activities) || !Activities.IsInitialized ||
            !GodotObject.IsInstanceValid(Reactions) ||
            !GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException("HeadLookAtComponent dependencies are incomplete.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy expression profile: {string.Join("; ", errors)}");
        }

        Reseed(Buddy.AutonomousMotion.Seed);
        Buddy.AutonomyReseeded += Reseed;
        DamagePipeline.ImpactAccepted += OnImpactAccepted;
        _lastRoutedTick = Buddy.RoutedTicks;
        IsInitialized = true;
    }

    public override void _ExitTree()
    {
        if (BuildInfo.IsDebugBuild && _tracedSource != LookAtSource.Rest)
            TraceSource("end", _tracedSource, "node_exit");

        if (!IsInitialized)
        {
            return;
        }

        if (GodotObject.IsInstanceValid(Buddy))
        {
            Buddy.AutonomyReseeded -= Reseed;
        }

        if (GodotObject.IsInstanceValid(DamagePipeline))
        {
            DamagePipeline.ImpactAccepted -= OnImpactAccepted;
        }
    }

    /// <summary>Rebuilds the ambient glance stream from the shared seed (own salted stream).</summary>
    public void Reseed(ulong seed)
    {
        if (BuildInfo.IsDebugBuild && _tracedSource != LookAtSource.Rest && _model is not null)
            TraceSource("end", _tracedSource, "reseeded");

        _model = new LookAtModel(
            new SeededRandomSource(seed ^ LookAtStreamSalt),
            Profile.ToData().ToLookAtParameters());
        _tracedSource = LookAtSource.Rest;
    }

    /// <summary>
    /// Samples current semantics and advances the model; returns the eased head angles in
    /// degrees. Called by the presenter once per rendered frame; allocation-free.
    /// </summary>
    public LookAtAngles Evaluate(double deltaSeconds)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("HeadLookAtComponent used before initialization.");
        }

        // The simulation's routed clock, not the engine frame counter (see BuddyRoot):
        // glance cadence and impact memory must hold still while the sim is held.
        long now = Buddy.RoutedTicks;
        int ticksElapsed = (int)Math.Clamp(now - _lastRoutedTick, 0, int.MaxValue);
        _lastRoutedTick = now;

        // Engagement is sampled exactly as the facing controller samples it: the cursor is
        // watched only while an interaction is actually engaged (the owner-resolved rule —
        // plain idle never tracks the cursor). The range cutoff itself lives in the model.
        bool engaged = false;
        Vector2 cursor = Vector2.Zero;
        ToolId tool = DamagePipeline.SelectedTool;
        if ((tool == ToolId.Pet || tool == ToolId.Tickle) &&
            CareStroke.IsHeld && CareStroke.LastContactValid)
        {
            engaged = true;
            cursor = CareStroke.Cursor;
        }
        else if (CursorTools.DrivesTool(tool) && CursorTools.HasCursor)
        {
            engaged = true;
            cursor = CursorTools.Cursor;
        }

        // Two item targets share this input: the eaten item riding the hand socket, and the
        // loose object the buddy is currently committed to — including one the player is still
        // carrying, so the head tracks the ball about to be thrown. Eating wins, because the
        // item is already in the mouth.
        bool eatingItem = Activities.Current == ActivityId.Eat &&
            Activities.ItemSocket.GetChildCount() > 0;
        bool watchingObject = !eatingItem &&
            GodotObject.IsInstanceValid(Buddy.ObjectInteraction) &&
            Buddy.ObjectInteraction.HasWatchTarget;
        bool itemValid = eatingItem || watchingObject;
        Vector2 item = eatingItem
            ? WorldPlaneMapping.To2D(Activities.ItemSocket.GlobalPosition)
            : watchingObject ? Buddy.ObjectInteraction.WatchTargetPosition : Vector2.Zero;

        long ticksSinceImpact = _lastImpactTick == long.MinValue
            ? long.MaxValue
            : now - _lastImpactTick;

        Vector2 head = Buddy.Rig.Head.GlobalPosition;
        var inputs = new LookAtInputs(
            engaged,
            cursor.X, cursor.Y,
            itemValid,
            item.X, item.Y,
            (int)Math.Clamp(ticksSinceImpact, 0, int.MaxValue),
            _lastImpactPoint.X, _lastImpactPoint.Y,
            Mathf.IsZeroApprox(Buddy.CurrentDriveIntent.WalkDirection),
            // A refusal owns the head exactly like a high-priority reaction face does: the gaze
            // rests dead ahead — at the player the buddy is saying no to — so an ambient glance
            // cannot wander off mid-shake (owner instruction 2026-07-29).
            Profile.SuppressesLookAt(Reactions.CurrentFace) || Buddy.Activity.IsRefusing,
            head.X, head.Y);
        LookAtAngles angles = _model.Update(inputs, ticksElapsed, deltaSeconds);
        LookAtSource after = _model.CurrentSource;
        if (BuildInfo.IsDebugBuild && after != _tracedSource)
        {
            if (_tracedSource != LookAtSource.Rest)
                TraceSource("end", _tracedSource, "source_changed");
            if (after != LookAtSource.Rest)
                TraceSource("start", after, "selected");
            _tracedSource = after;
        }

        return angles;
    }

    private void TraceSource(string @event, LookAtSource source, string reason) =>
        Log.Debug("AnimationTrace",
            $"event={@event} lane=head.look name={source.ToString().ToLowerInvariant()} " +
            $"reason={reason} tick={Buddy.RoutedTicks} frame={Engine.GetProcessFrames()} " +
            $"yaw={_model.CurrentYawDegrees:0.###} pitch={_model.CurrentPitchDegrees:0.###} " +
            $"activity={Activities.Current} face={Reactions.CurrentFace}");

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        _lastImpactTick = Buddy.RoutedTicks;
        _lastImpactPoint = impact.Point;
    }
}

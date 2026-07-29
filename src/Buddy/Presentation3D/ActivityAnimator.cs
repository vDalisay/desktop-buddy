using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// M3.6 Task 3 activity animator: one manual-mode <see cref="AnimationPlayer"/> playing
/// typed clips whose value tracks animate six presentation-proxy nodes — never a socket or a
/// body. The presenter reads each proxy position as that part's authored offset and the
/// refusal-only head yaw as a bounded visual rotation, so activities decorate the physics
/// truth and can never mutate it. Clips are built ONCE at initialization
/// from the typed amplitudes in <see cref="BuddyExpressionProfile"/> (authored as data,
/// not code literals; nothing is created per frame). Selection and walk-phase math live
/// engine-free in <see cref="ActivitySelector"/>: walk dressing derives its cycle from
/// MEASURED torso travel, so steps match speed and freeze at rest. Behavior-backed
/// requests arrive through <see cref="BuddyRoot.BehaviorActivityChanged"/>; this
/// presentation component never writes gameplay state.
/// </summary>
[GlobalClass]
public partial class ActivityAnimator : Node3D
{
    private const float WalkClipLength = 1.0f;

    [Export] public BuddyRoot Buddy { get; set; } = null!;
    [Export] public BuddyVisualPresenter Presenter { get; set; } = null!;
    [Export] public BuddyExpressionProfile Profile { get; set; } = null!;

    private readonly Node3D[] _proxies = new Node3D[PuppetRigProfile.RequiredPartCount];
    private AnimationPlayer _player = null!;
    private ActivitySelector _selector = null!;
    private float _refuseClipLength = 1.0f;

    public bool IsInitialized { get; private set; }
    public Node3D ItemSocket { get; private set; } = null!;
    public ActivityId Current => IsInitialized ? _selector.Current : ActivityId.None;
    public float WalkPhase => IsInitialized ? _selector.WalkPhase : 0.0f;
    public string CurrentClipName => IsInitialized ? (string)_player.CurrentAnimation : string.Empty;

    public void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized ||
            !GodotObject.IsInstanceValid(Presenter) || !Presenter.IsInitialized ||
            !GodotObject.IsInstanceValid(Profile))
        {
            throw new InvalidOperationException("ActivityAnimator dependencies are incomplete.");
        }

        Godot.Collections.Array<string> errors = Profile.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid buddy expression profile: {string.Join("; ", errors)}");
        }

        ActivityTuningData tuning = Profile.ToActivityData();
        _selector = new ActivitySelector(tuning.ToActivityParameters());
        Buddy.BehaviorActivityChanged += OnBehaviorActivityChanged;
        Buddy.Activity.EatBiteCompleted += OnEatBiteCompleted;

        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        for (int index = 0; index < _proxies.Length; index++)
        {
            var proxy = new Node3D
            {
                Name = ProxyName((BuddyPartId)index),
                PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
            };
            AddChild(proxy);
            _proxies[index] = proxy;
        }

        _player = new AnimationPlayer
        {
            Name = "ActivityPlayer",
            CallbackModeProcess = AnimationMixer.AnimationCallbackModeProcess.Manual,
        };
        AddChild(_player);
        var library = new AnimationLibrary();
        library.AddAnimation(ClipNameFor(ActivityId.IdleBreathe), BuildBreatheClip(tuning));
        library.AddAnimation(ClipNameFor(ActivityId.WalkCycle), BuildWalkClip(tuning));
        library.AddAnimation(ClipNameFor(ActivityId.JumpAnticipation), BuildJumpClip(tuning));
        library.AddAnimation(ClipNameFor(ActivityId.Wave), BuildWaveClip(tuning));
        library.AddAnimation(ClipNameFor(ActivityId.Eat), BuildEatClip(tuning));
        Animation refuse = BuildRefuseClip(tuning);
        _refuseClipLength = refuse.Length;
        library.AddAnimation(ClipNameFor(ActivityId.Refuse), refuse);
        _player.AddAnimationLibrary(string.Empty, library);

        // Presentation-only food follows the midpoint of both physical hand sockets.
        ItemSocket = new Node3D
        {
            Name = "ItemSocket",
            PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit,
        };
        AddChild(ItemSocket);

        IsInitialized = true;
    }

    /// <summary>Clip mapping used by scenarios: every real activity resolves to a clip.</summary>
    public static string ClipNameFor(ActivityId activity) => activity switch
    {
        ActivityId.IdleBreathe => "idle_breathe",
        ActivityId.WalkCycle => "walk_cycle",
        ActivityId.JumpAnticipation => "jump_anticipation",
        ActivityId.Wave => "wave",
        ActivityId.Eat => "eat",
        ActivityId.Refuse => "refuse",
        _ => string.Empty,
    };

    public bool HasClip(ActivityId activity) =>
        IsInitialized && _player.HasAnimation(ClipNameFor(activity));

    private void OnBehaviorActivityChanged(ActivityId activity)
    {
        switch (activity)
        {
            case ActivityId.Eat:
                _selector.RequestEat(Buddy.Activity.RemainingTicks /
                    (double)Engine.PhysicsTicksPerSecond);
                break;
            case ActivityId.Refuse:
                // The behavior layer owns the refusal window; the shake covers all of it.
                _selector.RequestRefuse(Buddy.Activity.RemainingTicks /
                    (double)Engine.PhysicsTicksPerSecond);
                break;
            case ActivityId.Wave:
                _selector.RequestWave();
                break;
            case ActivityId.None:
                _selector.CancelRequests();
                ClearItemVisual();
                break;
        }
    }

    private void OnEatBiteCompleted(int completed, int total)
    {
        float remaining = Mathf.Clamp((total - completed) / (float)total, 0.0f, 1.0f);
        ItemSocket.Scale = Vector3.One * remaining;
        if (completed >= total)
            ClearItemVisual();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Buddy))
        {
            Buddy.BehaviorActivityChanged -= OnBehaviorActivityChanged;
            Buddy.Activity.EatBiteCompleted -= OnEatBiteCompleted;
        }
    }

    /// <summary>Attaches an item visual to the hand's ItemSocket (replacing any current).</summary>
    public void AttachItemVisual(Node3D visual)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("ActivityAnimator used before initialization.");
        }

        ClearItemVisual();
        ItemSocket.Scale = Vector3.One;
        visual.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Inherit;
        ItemSocket.AddChild(visual);
    }

    public void ClearItemVisual()
    {
        if (!IsInitialized)
        {
            return;
        }

        foreach (Node child in ItemSocket.GetChildren())
        {
            child.QueueFree();
        }
    }

    /// <summary>The authored (pre-clamp) offset for a part this frame.</summary>
    public Vector3 OffsetFor(int partIndex) => _proxies[partIndex].Position;

    /// <summary>
    /// Refusal-only yaw around a part's vertical axis, in radians. All clips except Refuse
    /// leave this at zero; the presenter composes it on the visual head only.
    /// </summary>
    public float YawRadiansFor(int partIndex) => _proxies[partIndex].Rotation.Y;

    /// <summary>The complete authored proxy rotation for scenario verification.</summary>
    public Vector3 RotationFor(int partIndex) => _proxies[partIndex].Rotation;

    /// <summary>
    /// Advances selection and clip playback for this rendered frame. Called by the
    /// presenter before it resolves offsets; allocation-free after initialization.
    /// </summary>
    public void Evaluate(double deltaSeconds, bool performanceActive)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("ActivityAnimator used before initialization.");
        }

        var inputs = new ActivityInputs(
            performanceActive,
            MathF.Abs(Buddy.Rig.Torso.LinearVelocity.X),
            Buddy.CurrentDriveIntent.JumpRequested);
        // Refusal duration is authoritative routed-tick state. Pin selection to that state
        // instead of allowing one long rendered frame to expire a parallel seconds timer
        // while capped physics advances only a few ticks.
        ActivityId activity;
        if (performanceActive && Buddy.Activity.IsRefusing)
        {
            _selector.RequestRefuse(Math.Max(
                Buddy.Activity.RemainingTicks / (double)Engine.PhysicsTicksPerSecond,
                1.0 / Engine.PhysicsTicksPerSecond));
            activity = _selector.Update(inputs, 0.0);
        }
        else
        {
            activity = _selector.Update(inputs, deltaSeconds);
        }

        if (activity == ActivityId.None)
        {
            if (!string.IsNullOrEmpty(_player.CurrentAnimation))
            {
                _player.Stop();
            }

            for (int index = 0; index < _proxies.Length; index++)
            {
                _proxies[index].Position = Vector3.Zero;
                _proxies[index].Rotation = Vector3.Zero;
            }

            return;
        }

        string clip = ClipNameFor(activity);
        if (activity == ActivityId.Refuse)
        {
            // Refusal is rotation-only. Clear any sampled breathe/walk head translation from
            // the previous clip before seeking the yaw track.
            _proxies[(int)BuddyPartId.Head].Position = Vector3.Zero;
        }
        else
        {
            // Other clips have no rotation track. Clear the last seeked refusal sample so
            // ambient/walk/eat cannot inherit a residual over-the-shoulder look.
            _proxies[(int)BuddyPartId.Head].Rotation = Vector3.Zero;
        }
        if (_player.CurrentAnimation != clip)
        {
            _player.Play(clip);
        }

        if (activity == ActivityId.WalkCycle)
        {
            // Phase-seeked, not time-advanced: the cycle position is measured travel.
            _player.Seek(_selector.WalkPhase * WalkClipLength, update: true);
        }
        else if (activity == ActivityId.Eat)
        {
            _player.Seek(Buddy.Activity.EatCycleProgress, update: true);
        }
        else if (activity == ActivityId.Refuse)
        {
            // Seeked, not advanced: the damped yaw fills the behavior-owned refusal window
            // however the behavior and expression profiles are tuned.
            _player.Seek(Buddy.Activity.RefuseProgress * _refuseClipLength, update: true);
        }
        else
        {
            _player.Advance(deltaSeconds);
        }
    }

    /// <summary>Called after the presenter resolves both hand sockets for this frame.</summary>
    public void SyncItemSocket()
    {
        if (!IsInitialized)
            return;
        Node3D leftHand = Presenter.GetPartSocket(BuddyPartId.LeftHand);
        Node3D rightHand = Presenter.GetPartSocket(BuddyPartId.RightHand);
        ItemSocket.GlobalPosition = (leftHand.GlobalPosition + rightHand.GlobalPosition) * 0.5f;
        ItemSocket.GlobalPosition += Vector3.Up *
            (Presenter.Profile.EatItemLiftPixels * Buddy.Activity.EatLift);
        ItemSocket.GlobalPosition += Vector3.Back *
            (Presenter.Profile.EatItemFrontOffset * Buddy.Activity.EatLift);
    }

    private static string ProxyName(BuddyPartId id) => id switch
    {
        BuddyPartId.Head => "ProxyHead",
        BuddyPartId.Torso => "ProxyTorso",
        BuddyPartId.LeftHand => "ProxyLeftHand",
        BuddyPartId.RightHand => "ProxyRightHand",
        BuddyPartId.LeftFoot => "ProxyLeftFoot",
        BuddyPartId.RightFoot => "ProxyRightFoot",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown part."),
    };

    private static string TrackPath(BuddyPartId id) => $"{ProxyName(id)}:position";
    private static string RotationTrackPath(BuddyPartId id) => $"{ProxyName(id)}:rotation";

    private static int AddPositionTrack(Animation animation, BuddyPartId id)
    {
        int track = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(track, TrackPath(id));
        animation.TrackSetInterpolationType(track, Animation.InterpolationType.Cubic);
        return track;
    }

    private static int AddRotationTrack(Animation animation, BuddyPartId id)
    {
        int track = animation.AddTrack(Animation.TrackType.Value);
        animation.TrackSetPath(track, RotationTrackPath(id));
        animation.TrackSetInterpolationType(track, Animation.InterpolationType.Cubic);
        return track;
    }

    private static void Key(Animation animation, int track, double time, float x, float y)
        => animation.TrackInsertKey(track, time, new Vector3(x, y, 0.0f));

    private static void YawKey(
        Animation animation,
        int track,
        double time,
        float yawDegrees) =>
        animation.TrackInsertKey(
            track,
            time,
            new Vector3(0.0f, Mathf.DegToRad(yawDegrees), 0.0f));

    /// <summary>Slow torso/head rise-and-fall; the quiet default sign of life.</summary>
    private static Animation BuildBreatheClip(in ActivityTuningData tuning)
    {
        float amplitude = tuning.BreatheAmplitude;
        double length = tuning.BreatheSeconds;
        var animation = new Animation { Length = (float)length, LoopMode = Animation.LoopModeEnum.Linear };
        int torso = AddPositionTrack(animation, BuddyPartId.Torso);
        Key(animation, torso, 0.0, 0.0f, 0.0f);
        Key(animation, torso, length * 0.5, 0.0f, amplitude);
        Key(animation, torso, length, 0.0f, 0.0f);
        int head = AddPositionTrack(animation, BuddyPartId.Head);
        Key(animation, head, 0.0, 0.0f, 0.0f);
        Key(animation, head, length * 0.55, 0.0f, amplitude * 0.6f);
        Key(animation, head, length, 0.0f, 0.0f);
        return animation;
    }

    /// <summary>Alternating foot lifts, a double torso bob, and light counter-swinging
    /// hands over one normalized cycle; the phase (measured travel) picks the pose.</summary>
    private static Animation BuildWalkClip(in ActivityTuningData tuning)
    {
        float amplitude = tuning.WalkBobAmplitude;
        var animation = new Animation { Length = WalkClipLength, LoopMode = Animation.LoopModeEnum.Linear };

        int leftFoot = AddPositionTrack(animation, BuddyPartId.LeftFoot);
        Key(animation, leftFoot, 0.0, 0.0f, 0.0f);
        Key(animation, leftFoot, 0.25, 0.0f, amplitude);
        Key(animation, leftFoot, 0.5, 0.0f, 0.0f);
        Key(animation, leftFoot, 1.0, 0.0f, 0.0f);

        int rightFoot = AddPositionTrack(animation, BuddyPartId.RightFoot);
        Key(animation, rightFoot, 0.0, 0.0f, 0.0f);
        Key(animation, rightFoot, 0.5, 0.0f, 0.0f);
        Key(animation, rightFoot, 0.75, 0.0f, amplitude);
        Key(animation, rightFoot, 1.0, 0.0f, 0.0f);

        int torso = AddPositionTrack(animation, BuddyPartId.Torso);
        Key(animation, torso, 0.0, 0.0f, 0.0f);
        Key(animation, torso, 0.25, 0.0f, amplitude * 0.5f);
        Key(animation, torso, 0.5, 0.0f, 0.0f);
        Key(animation, torso, 0.75, 0.0f, amplitude * 0.5f);
        Key(animation, torso, 1.0, 0.0f, 0.0f);

        int leftHand = AddPositionTrack(animation, BuddyPartId.LeftHand);
        Key(animation, leftHand, 0.0, 0.0f, 0.0f);
        Key(animation, leftHand, 0.25, amplitude * 0.4f, 0.0f);
        Key(animation, leftHand, 0.75, amplitude * -0.4f, 0.0f);
        Key(animation, leftHand, 1.0, 0.0f, 0.0f);

        int rightHand = AddPositionTrack(animation, BuddyPartId.RightHand);
        Key(animation, rightHand, 0.0, 0.0f, 0.0f);
        Key(animation, rightHand, 0.25, amplitude * -0.4f, 0.0f);
        Key(animation, rightHand, 0.75, amplitude * 0.4f, 0.0f);
        Key(animation, rightHand, 1.0, 0.0f, 0.0f);
        return animation;
    }

    /// <summary>Pre-liftoff squash: torso and head dip before the real physical jump;
    /// the flight itself stays pure Tracking.</summary>
    private static Animation BuildJumpClip(in ActivityTuningData tuning)
    {
        float squash = tuning.JumpSquashAmplitude;
        double length = tuning.JumpAnticipationSeconds;
        var animation = new Animation { Length = (float)length };
        int torso = AddPositionTrack(animation, BuddyPartId.Torso);
        Key(animation, torso, 0.0, 0.0f, 0.0f);
        Key(animation, torso, length * 0.6, 0.0f, -squash);
        Key(animation, torso, length, 0.0f, -squash * 0.4f);
        int head = AddPositionTrack(animation, BuddyPartId.Head);
        Key(animation, head, 0.0, 0.0f, 0.0f);
        Key(animation, head, length * 0.6, 0.0f, -squash * 0.7f);
        Key(animation, head, length, 0.0f, -squash * 0.3f);
        return animation;
    }

    /// <summary>One-shot right-hand wave: raise, two side beats, settle.</summary>
    private static Animation BuildWaveClip(in ActivityTuningData tuning)
    {
        float amplitude = tuning.WaveAmplitude;
        double length = tuning.WaveSeconds;
        var animation = new Animation { Length = (float)length };
        int hand = AddPositionTrack(animation, BuddyPartId.RightHand);
        Key(animation, hand, 0.0, 0.0f, 0.0f);
        Key(animation, hand, length * 0.25, amplitude * 0.3f, amplitude);
        Key(animation, hand, length * 0.45, amplitude * 0.8f, amplitude);
        Key(animation, hand, length * 0.65, amplitude * -0.2f, amplitude);
        Key(animation, hand, length * 0.8, amplitude * 0.5f, amplitude);
        Key(animation, hand, length, 0.0f, 0.0f);
        return animation;
    }

    /// <summary>
    /// "No thanks": a smooth damped yaw around the neck's vertical axis, as though the buddy
    /// alternately looks over each shoulder. Four alternating extremes are the maximum:
    /// left at the authored angle, then progressively smaller right/left/right turns, followed
    /// by neutral. Cubic interpolation reverses at the ends without holds and crosses the
    /// middle without a key or pause (owner correction 2026-07-30).
    /// </summary>
    private static Animation BuildRefuseClip(in ActivityTuningData tuning)
    {
        float yaw = tuning.RefuseYawDegrees;
        double length = tuning.WaveSeconds;
        var animation = new Animation { Length = (float)length };
        int head = AddRotationTrack(animation, BuddyPartId.Head);
        YawKey(animation, head, 0.0, 0.0f);
        YawKey(animation, head, length * 0.12, -yaw);
        YawKey(animation, head, length * 0.34, yaw * 0.83f);
        YawKey(animation, head, length * 0.56, -yaw * 0.67f);
        YawKey(animation, head, length * 0.78, yaw * 0.40f);
        YawKey(animation, head, length, 0.0f);
        return animation;
    }

    /// <summary>One normalized, subtle downward head bob at the bite moment.</summary>
    private static Animation BuildEatClip(in ActivityTuningData tuning)
    {
        float chew = tuning.ChewAmplitude;
        const float loop = 1.0f;
        var animation = new Animation { Length = loop, LoopMode = Animation.LoopModeEnum.Linear };
        int head = AddPositionTrack(animation, BuddyPartId.Head);
        Key(animation, head, 0.0, 0.0f, 0.0f);
        Key(animation, head, 0.4, 0.0f, 0.0f);
        Key(animation, head, 0.55, 0.0f, -chew);
        Key(animation, head, 0.7, 0.0f, 0.0f);
        Key(animation, head, loop, 0.0f, 0.0f);
        return animation;
    }
}

using System;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Laboratory;

public readonly record struct LaboratoryControlSnapshot(
    bool IsPaused,
    bool SingleStepPending,
    double TimeScale,
    Consciousness Consciousness,
    ulong AutonomySeed,
    long RoutedPhysicsTicks);

/// <summary>
/// Development-only keyboard/API controls for the real laboratory composition.
/// The lab root asks whether to route each fixed tick; this component owns body
/// freezing around pause/single-step and restores the engine time scale on exit.
/// </summary>
[GlobalClass]
public partial class LaboratoryControlComponent : Node
{
    private const double MinimumTimeScale = 0.05;
    private const double MaximumTimeScale = 4.0;

    private bool _singleStepRequested;
    private bool _freezeAfterStep;
    private double _originalTimeScale = 1.0;

    public event Action? PresentationToggleRequested;

    [Export] public BuddyRoot Buddy { get; set; } = null!;

    // Optional: when present, the pause also holds the M3.6 performance layer so a paused
    // buddy is visually still. Labs without a 3D presenter simply leave it unset.
    [Export] public Buddy.Presentation3D.BuddyVisualPresenter? Presenter { get; set; }

    // Optional: tool-selection hotkeys route here when the owning lab wires the
    // interaction pipeline (M3); the dual-profile lab leaves this unset.
    [Export] public InteractionDamageComponent? Pipeline { get; set; }

    // Optional (M3.6 Task 6): expressive-performance trigger keys. Debug builds only —
    // these drive presentation seams (Class B activity requests, the facing development
    // side), never gameplay state.
    [Export] public Buddy.Presentation3D.ActivityAnimator? Activities { get; set; }
    [Export] public Buddy.Presentation3D.FacingController? Facing { get; set; }

    public bool IsInitialized { get; private set; }
    public bool IsPaused { get; private set; }
    public double TimeScale { get; private set; } = 1.0;
    public ulong AutonomySeed { get; private set; } = 1;
    public long RoutedPhysicsTicks { get; private set; }
    public Key LastControlKey { get; private set; } = Key.None;

    public LaboratoryControlSnapshot Snapshot => new(
        IsPaused,
        _singleStepRequested,
        TimeScale,
        Buddy.CurrentConsciousness,
        AutonomySeed,
        RoutedPhysicsTicks);

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Buddy) || !Buddy.IsInitialized)
        {
            throw new InvalidOperationException(
                "LaboratoryControlComponent requires an initialized buddy composition.");
        }

        _originalTimeScale = Engine.TimeScale;
        TimeScale = Engine.TimeScale;
        AutonomySeed = Buddy.AutonomousMotion.Seed;
        IsInitialized = true;
    }

    /// <summary>Called only by <c>BuddyLab._PhysicsProcess</c>.</summary>
    public bool BeginPhysicsTick()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("Laboratory controls were ticked before initialization.");
        }

        if (!IsPaused)
        {
            return true;
        }

        if (_freezeAfterStep)
        {
            SetBodiesFrozen(true);
            _freezeAfterStep = false;
            return false;
        }

        if (!_singleStepRequested)
        {
            return false;
        }

        _singleStepRequested = false;
        _freezeAfterStep = true;
        SetBodiesFrozen(false);
        return true;
    }

    public void NotifyPhysicsTickRouted()
    {
        RoutedPhysicsTicks++;
    }

    public void SetPaused(bool paused)
    {
        if (IsPaused == paused)
        {
            return;
        }

        IsPaused = paused;
        _singleStepRequested = false;
        _freezeAfterStep = false;
        SetBodiesFrozen(paused);
        HoldPresentation(paused);
    }

    public void RequestSingleStep()
    {
        if (IsPaused)
        {
            _singleStepRequested = true;
        }
    }

    public void SetTimeScale(double value)
    {
        if (!double.IsFinite(value) || value < MinimumTimeScale || value > MaximumTimeScale)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Laboratory time scale must be within {MinimumTimeScale}..{MaximumTimeScale}.");
        }

        TimeScale = value;
        Engine.TimeScale = value;
    }

    public void ToggleConsciousness()
    {
        Buddy.SetConsciousness(Buddy.CurrentConsciousness == Consciousness.Conscious
            ? Consciousness.Unconscious
            : Consciousness.Conscious);
    }

    public void Reseed(ulong seed)
    {
        AutonomySeed = seed;
        Buddy.ReseedAutonomy(seed);
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsInitialized || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        LastControlKey = key.PhysicalKeycode;

        switch (key.PhysicalKeycode)
        {
            case Key.P:
                SetPaused(!IsPaused);
                break;
            case Key.Period:
                RequestSingleStep();
                break;
            case Key.U when key.ShiftPressed:
                Reseed(AutonomySeed + 1);
                break;
            case Key.U:
                ToggleConsciousness();
                break;
            case Key.Key1:
                SetTimeScale(0.25);
                break;
            case Key.Key2:
                SetTimeScale(0.5);
                break;
            case Key.Key3:
                SetTimeScale(1.0);
                break;
            case Key.Key4:
                SetTimeScale(2.0);
                break;
            case Key.G when HasPipeline:
                Pipeline!.SelectTool(ToolId.Grab);
                break;
            case Key.B when HasPipeline:
                Pipeline!.SelectTool(ToolId.BoxingGlove);
                break;
            case Key.T when HasPipeline:
                Pipeline!.SelectTool(ToolId.Tickle);
                break;
            case Key.F when HasPipeline:
                Pipeline!.SelectTool(ToolId.Pet);
                break;
            case Key.V:
                PresentationToggleRequested?.Invoke();
                break;
            case Key.E when HasActivities:
                ToggleEat();
                break;
            case Key.Q when HasActivities:
                Buddy.SetBehaviorActivity(ActivityId.Wave);
                break;
            case Key.Z when HasFacing:
                Facing!.SetDevelopmentSide(-1);
                break;
            case Key.X when HasFacing:
                Facing!.SetDevelopmentSide(1);
                break;
            case Key.C when HasFacing:
                Facing!.SetDevelopmentSide(0);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    private bool HasPipeline => Pipeline is not null && GodotObject.IsInstanceValid(Pipeline);

    private bool HasActivities => BuildInfo.IsDebugBuild &&
        Activities is { IsInitialized: true } && GodotObject.IsInstanceValid(Activities);

    private bool HasFacing => BuildInfo.IsDebugBuild &&
        Facing is { IsInitialized: true } && GodotObject.IsInstanceValid(Facing);

    /// <summary>
    /// Eat needs something in the hand to be legible, so the lab key owns a throwaway
    /// item visual: first press attaches it and requests the clip, second press cancels
    /// the request and clears the socket.
    /// </summary>
    public void ToggleEat()
    {
        if (!HasActivities)
        {
            return;
        }

        ActivityAnimator activities = Activities!;
        if (Buddy.Activity.Current == ActivityId.Eat)
        {
            Buddy.SetBehaviorActivity(ActivityId.None);
            activities.ClearItemVisual();
            return;
        }

        activities.AttachItemVisual(new MeshInstance3D
        {
            Name = "LabEatItemVisual",
            Mesh = new SphereMesh { Radius = 3.0f, Height = 6.0f },
        });
        Buddy.SetBehaviorActivity(ActivityId.Eat);
    }

    /// <summary>True while the eat key's throwaway item visual sits in the hand socket.</summary>
    public bool IsEatKeyItemAttached => HasActivities && Activities!.ItemSocket.GetChildCount() > 0;

    public override void _ExitTree()
    {
        if (!IsInitialized)
        {
            return;
        }

        Engine.TimeScale = _originalTimeScale;
        if (IsPaused)
        {
            SetBodiesFrozen(false);
        }
    }

    private void HoldPresentation(bool held)
    {
        if (Presenter is { IsInitialized: true })
        {
            Presenter.SetPresentationHeld(held);
        }
    }

    private void SetBodiesFrozen(bool frozen)
    {
        foreach (PuppetPartBody body in Buddy.Rig.Parts)
        {
            body.Freeze = frozen;
            if (!frozen)
            {
                body.Sleeping = false;
            }

            body.ResetPhysicsInterpolation();
        }
    }
}

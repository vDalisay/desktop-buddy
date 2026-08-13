using System;
using DesktopBuddy.Domain.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

public enum HardRecoveryReason
{
    Timeout,
    InvalidState,
    OutOfBounds,
}

/// <summary>
/// Owns inability timing and the single auditable hard-pose reset seam. Future
/// grab/status/pain components subscribe to <see cref="HardRecovered"/> to clear
/// their transient state without coupling that work into this component.
/// </summary>
[GlobalClass]
public partial class RecoveryComponent : Node
{
    private const float ContainableEscapeMarginPx = 128.0f;

    private readonly RecoveryClock _clock = new();

    [Export] public PuppetRig Rig { get; set; } = null!;
    [Export] public StandingDetector Standing { get; set; } = null!;
    [Export] public Rect2 SafeBounds { get; set; } = new(0, 0, 480, 360);
    [Export] public Vector2 SafePoseOrigin { get; set; } = new(240, 260);

    public event Action<HardRecoveryReason>? HardRecovered;
    public event Action? SessionResumed;

    public RecoveryClockState State => _clock.State;
    public int HardRecoveryCount { get; private set; }
    public HardRecoveryReason? LastHardRecoveryReason { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized ||
            !GodotObject.IsInstanceValid(Standing) || !Standing.IsInitialized)
        {
            throw new InvalidOperationException("RecoveryComponent requires an initialized rig and standing detector.");
        }

        if (!SafeBounds.HasArea() || !SafePoseOrigin.IsFinite())
        {
            throw new InvalidOperationException("RecoveryComponent requires finite safe bounds and pose origin.");
        }

        IsInitialized = true;
    }

    public void PhysicsTick(bool conscious)
    {
        HardRecoveryReason? immediateReason = FindImmediateRecoveryReason();
        if (immediateReason is HardRecoveryReason reason)
        {
            PerformHardRecovery(reason);
            return;
        }

        RecoveryClockState state = _clock.Tick(Standing.Snapshot.IsStable, conscious);
        if (state.HardRecoveryDue)
        {
            PerformHardRecovery(HardRecoveryReason.Timeout);
        }
    }

    public bool AllBodiesInsideSafeBounds()
    {
        foreach (PuppetPartBody body in Rig.Parts)
        {
            Vector2 position = body.GlobalPosition;
            if (position.X < SafeBounds.Position.X ||
                position.Y < SafeBounds.Position.Y ||
                position.X > SafeBounds.End.X ||
                position.Y > SafeBounds.End.Y)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Starts a loaded session from the ordinary safe standing pose and asks
    /// transient-state owners to clear themselves. This is not a hard recovery:
    /// it increments no recovery statistic and emits no recovery reason.
    /// </summary>
    public void ResetForSessionResume()
    {
        if (!IsInitialized)
            throw new InvalidOperationException("RecoveryComponent is not initialized.");
        _clock.Reset();
        Standing.Reset();
        Rig.ResetToSafePose(SafePoseOrigin);
        SessionResumed?.Invoke();
    }

    /// <summary>
    /// Whether every escaped part is close enough to the room to be a tunnelling artifact
    /// rather than genuinely lost state. One 120 Hz step of the fastest thing in the game
    /// (a fully charged bat tip, 6000 px/s) covers 50 px, so a part further out than this
    /// margin did not get there by being hit, and the whole rig is re-posed as before.
    /// </summary>
    private bool EscapeIsContainable()
    {
        Rect2 reachable = SafeBounds.Grow(ContainableEscapeMarginPx);
        foreach (PuppetPartBody body in Rig.Parts)
        {
            if (!reachable.HasPoint(body.GlobalPosition))
            {
                return false;
            }
        }

        return true;
    }

    private HardRecoveryReason? FindImmediateRecoveryReason()
    {
        if (!Rig.AllBodiesFinite())
        {
            return HardRecoveryReason.InvalidState;
        }

        return AllBodiesInsideSafeBounds() ? null : HardRecoveryReason.OutOfBounds;
    }

    private void PerformHardRecovery(HardRecoveryReason reason)
    {
        _clock.Reset();
        Standing.Reset();
        if (reason == HardRecoveryReason.OutOfBounds && EscapeIsContainable())
        {
            // A hard bat/shotgun hit can push a part through the 16 px wall in one 120 Hz
            // step (no CCD on the parts), and re-posing the whole rig at the safe origin
            // teleported the buddy to the middle of the room mid-flight — the launch read
            // as a respawn (owner report 2026-08-13). A tunnelled part is a containment
            // problem, not a broken simulation: clamp it back against the inner wall face
            // and drop its outward velocity, exactly as a room resize does. Parts still
            // inside are untouched, so the rest of the buddy keeps flying.
            foreach (PuppetPartBody body in Rig.Parts)
            {
                Sandbox.PuppetRoomContainmentComponent.CorrectBody(body, SafeBounds);
            }
        }
        else
        {
            Rig.ResetToSafePose(SafePoseOrigin);
        }

        HardRecoveryCount++;
        LastHardRecoveryReason = reason;
        HardRecovered?.Invoke(reason);
    }
}

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
        Rig.ResetToSafePose(SafePoseOrigin);
        HardRecoveryCount++;
        LastHardRecoveryReason = reason;
        HardRecovered?.Invoke(reason);
    }
}

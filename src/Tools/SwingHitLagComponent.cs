using System;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

public readonly record struct SwingHitLagStarted(
    int SwingEpoch,
    float ReleasedCharge,
    int DurationTicks,
    BuddyPart? StruckPart,
    bool IsLooseObjectHit);

/// <summary>
/// Owns the home-run whole-game freeze. The composition root asks this component
/// whether the current fixed frame is frozen before routing any gameplay work;
/// disabling the 2D physics server also prevents Godot's solver from advancing
/// velocities while that routed tick is withheld.
/// </summary>
[GlobalClass]
public partial class SwingHitLagComponent : Node
{
    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;
    [Export] public CursorToolController CursorTools { get; set; } = null!;

    public event Action<SwingHitLagStarted>? Started;
    public event Action<bool>? Ended;

    public bool IsInitialized { get; private set; }
    public bool IsActive { get; private set; }
    public int TotalTicks { get; private set; }
    public int RemainingTicks { get; private set; }
    public int TriggerCount { get; private set; }
    public int CompletionCount { get; private set; }
    public int CancelCount { get; private set; }
    public int FrozenFrameCount { get; private set; }
    public ulong StartedUsec { get; private set; }
    public SwingHitLagStarted Current { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline) || !Pipeline.IsInitialized ||
            !GodotObject.IsInstanceValid(CursorTools) || !CursorTools.IsInitialized)
        {
            throw new InvalidOperationException(
                "SwingHitLagComponent requires initialized damage and cursor-tool components.");
        }

        Pipeline.ImpactAccepted += OnImpactAccepted;
        CursorTools.LooseObjectSwingHit += OnLooseObjectSwingHit;
        IsInitialized = true;
    }

    /// <summary>
    /// Called at the root routing gate on every engine physics frame. An active
    /// duration consumes exactly its authored number of frames. Completion is
    /// finalized on the following frame so the physics server remains inactive
    /// through the solver phase of the last frozen frame.
    /// </summary>
    public bool ConsumeFrozenPhysicsFrame()
    {
        if (!IsActive)
        {
            return false;
        }

        if (RemainingTicks > 0)
        {
            RemainingTicks--;
            FrozenFrameCount++;
            return true;
        }

        Finish(canceled: false);
        return false;
    }

    /// <summary>
    /// Idempotent fail-safe used by pointer exit, hard recovery, tool swap, and
    /// tree exit. The physics server is resumed only by the transition from
    /// active to inactive, so repeated cleanup signals cannot double-resume.
    /// </summary>
    public void Cancel()
    {
        if (!IsActive)
        {
            return;
        }

        CancelCount++;
        Finish(canceled: true);
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Pipeline))
        {
            Pipeline.ImpactAccepted -= OnImpactAccepted;
        }

        if (GodotObject.IsInstanceValid(CursorTools))
        {
            CursorTools.LooseObjectSwingHit -= OnLooseObjectSwingHit;
        }

        Cancel();
    }

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        if (impact.SwingEpoch <= 0)
        {
            return;
        }

        SwingToolProfile? profile = CursorTools.SwingProfileForContent(impact.ContentId);
        if (profile is null)
        {
            return;
        }

        int duration = ChargedSwing.HitLagTicks(
            impact.SwingCharge,
            profile.HitLagMinTicks,
            profile.HitLagMaxTicks);
        TryStart(new SwingHitLagStarted(
            impact.SwingEpoch,
            impact.SwingCharge,
            duration,
            impact.Part,
            IsLooseObjectHit: false));
    }

    private void OnLooseObjectSwingHit(LooseObjectSwingHit hit)
    {
        SwingImpactContext context = hit.Context;
        // The full-charge glint and object freeze deliberately share the exact
        // normalized endpoint. A one-tick-under-cap release must not freeze.
        if (context.Mode != SwingImpactMode.HomeRun || context.ReleasedCharge != 1.0f)
        {
            return;
        }

        SwingToolProfile? profile = CursorTools.SwingProfileForContent(hit.ContentId);
        if (profile is null)
        {
            return;
        }

        TryStart(new SwingHitLagStarted(
            context.SwingEpoch,
            context.ReleasedCharge,
            profile.HitLagMaxTicks,
            StruckPart: null,
            IsLooseObjectHit: true));
    }

    private void TryStart(SwingHitLagStarted started)
    {
        if (IsActive || started.DurationTicks <= 0)
        {
            return;
        }

        Current = started;
        TotalTicks = started.DurationTicks;
        RemainingTicks = started.DurationTicks;
        StartedUsec = Time.GetTicksUsec();
        IsActive = true;
        TriggerCount++;
        PhysicsServer2D.Singleton.SetActive(false);
        Started?.Invoke(started);
    }

    private void Finish(bool canceled)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        RemainingTicks = 0;
        PhysicsServer2D.Singleton.SetActive(true);
        if (!canceled)
        {
            CompletionCount++;
        }

        Ended?.Invoke(canceled);
    }
}

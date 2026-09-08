using System;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Interaction;
using DesktopBuddy.Persistence;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Production adapter from the Scene-owned runtime to the engine-free achievement rules.
/// Next Fest and Full Release compose this node; Initial Demo and itch never do. This adapter
/// performs local qualification only. Steam reconciliation is intentionally owned by a separate
/// full-release-only platform adapter so the Demo AppID can never receive achievement unlock calls.
/// </summary>
public sealed partial class AchievementBootstrap : Node
{
    private const double EvaluationSeconds = 0.5;

    private SandboxRoot _sandbox = null!;
    private SceneProgressCoordinator _scenes = null!;
    private IRunProgressPersistence _persistence = null!;
    private AchievementCoordinator _coordinator = null!;
    private double _evaluationCountdown;
    private long _observedPlayerRevision;
    private bool _flushRunning;

    public AchievementCoordinator Coordinator => _coordinator;

    public void Configure(SandboxRoot sandbox, RunContext context)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("AchievementBootstrap must be configured before entering the tree.");
        ArgumentNullException.ThrowIfNull(context);
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _scenes = context.SceneProgress
            ?? throw new ArgumentException("Achievement runtime requires Scene-owned progress.", nameof(context));
        _persistence = context.RunProgressPersistence;
        _coordinator = new AchievementCoordinator(_scenes.Player, _scenes.Work);
        _observedPlayerRevision = _scenes.Player.Revision;
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        if (_coordinator is null)
            throw new InvalidOperationException("AchievementBootstrap was not configured.");

        _sandbox.Pipeline.ImpactAccepted += OnImpactAccepted;
        _coordinator.Store.Qualified += OnQualified;
        EvaluateDurableState();
    }

    public override void _ExitTree()
    {
        if (_coordinator is null)
            return;
        if (GodotObject.IsInstanceValid(_sandbox) && GodotObject.IsInstanceValid(_sandbox.Pipeline))
            _sandbox.Pipeline.ImpactAccepted -= OnImpactAccepted;
        _coordinator.Store.Qualified -= OnQualified;
    }

    public override void _Process(double delta)
    {
        if (_coordinator is null)
            return;

        // Reset/rollback can move the authoritative account revision backwards. Process-local
        // rolling windows must not span that discontinuity.
        long revision = _scenes.Player.Revision;
        if (revision < _observedPlayerRevision)
            _coordinator.ResetTransientObservations();
        _observedPlayerRevision = revision;

        _evaluationCountdown -= Math.Max(0.0, delta);
        if (_evaluationCountdown > 0.0)
            return;
        _evaluationCountdown = EvaluationSeconds;
        EvaluateDurableState();
    }

    private void EvaluateDurableState()
    {
        _coordinator.EvaluateAccountState();
        foreach (BuddyIdentityState buddy in _scenes.BuddyIdentities())
            _coordinator.EvaluatePersistentState(buddy);
    }

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        // The current production Scene host still has one physical actor and requires the reserved
        // legacy-primary placement. Keep the attribution explicit instead of guessing a Buddy from
        // UI focus. When multi-actor spawning lands this adapter can resolve the actor from the
        // accepted-impact source without changing the domain rule API.
        _scenes.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? buddy);
        _coordinator.RecordDamage(
            impact.ContentId,
            impact.Pain,
            impact.MilliCredits,
            impact.TimeSeconds,
            buddy);
    }

    private void OnQualified(AchievementDefinition definition)
    {
        GD.Print($"ACHIEVEMENT_QUALIFIED {definition.SteamApiName} ({definition.DisplayName})");
        if (!_flushRunning)
            _ = FlushQualificationAsync();
    }

    private async Task FlushQualificationAsync()
    {
        _flushRunning = true;
        try
        {
            await _persistence.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // SceneProgressCoordinator remains dirty after failure; normal autosave/shutdown retries.
            Log.Error("Achievements", $"Qualification save failed; progress remains dirty: {exception.Message}");
        }
        finally
        {
            _flushRunning = false;
        }
    }
}

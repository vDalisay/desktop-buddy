using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Interaction;
using DesktopBuddy.Persistence;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Production adapter from the Scene-owned runtime to the engine-free achievement rules.
/// Next Fest and Full Release compose local qualification. Only a valid Full Release receives the
/// already-initialized Workshop-owned Steam bridge and may mirror qualified state to Steam.
/// </summary>
public sealed partial class AchievementBootstrap : Node
{
    private const double EvaluationSeconds = 0.5;
    private const double SteamPollSeconds = 5.0;

    private SandboxRoot _sandbox = null!;
    private SceneProgressCoordinator _scenes = null!;
    private IRunProgressPersistence _persistence = null!;
    private AchievementCoordinator _coordinator = null!;
    private Node? _initializedSteamBridge;
    private SteamAchievementPublisher? _publisher;
    private double _evaluationCountdown;
    private double _steamCountdown;
    private long _observedPlayerRevision;
    private int _flushRunning;
    private bool _steamSyncRequested;

    public AchievementCoordinator Coordinator => _coordinator;

    public void Configure(
        SandboxRoot sandbox,
        RunContext context,
        Node? initializedSteamBridge = null)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("AchievementBootstrap must be configured before entering the tree.");
        ArgumentNullException.ThrowIfNull(context);
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _scenes = context.SceneProgress
            ?? throw new ArgumentException("Achievement runtime requires Scene-owned progress.", nameof(context));
        _persistence = context.RunProgressPersistence;
        _coordinator = new AchievementCoordinator(_scenes.Player, _scenes.Work);
        _initializedSteamBridge = initializedSteamBridge;
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

        if (DemoScope.ActiveBuildScope.PublishesSteamAchievements)
        {
            _publisher = new SteamAchievementPublisher(
                _coordinator.Store,
                SteamAppIdentityResolver.Resolve(),
                _initializedSteamBridge);
            // Reconcile qualifications carried from Next Fest/offline play on the first valid Full
            // launch. If Steam is unavailable the local desired state simply remains authoritative.
            _publisher.TrySynchronize();
        }
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

        long revision = _scenes.Player.Revision;
        if (revision < _observedPlayerRevision)
            _coordinator.ResetTransientObservations();
        _observedPlayerRevision = revision;

        double acceptedDelta = Math.Max(0.0, delta);
        _evaluationCountdown -= acceptedDelta;
        if (_evaluationCountdown <= 0.0)
        {
            _evaluationCountdown = EvaluationSeconds;
            EvaluateDurableState();
        }

        if (_steamSyncRequested)
        {
            _steamSyncRequested = false;
            _publisher?.TrySynchronize();
        }

        _steamCountdown -= acceptedDelta;
        if (_steamCountdown <= 0.0)
        {
            _steamCountdown = SteamPollSeconds;
            _publisher?.TrySynchronize();
        }
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
        // legacy-primary placement. Keep attribution explicit instead of guessing UI focus. When
        // production multi-actor spawning lands, resolve the actor identity from the accepted hit.
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
        _steamSyncRequested = true;
        if (Interlocked.CompareExchange(ref _flushRunning, 1, 0) == 0)
            _ = FlushQualificationAsync();
    }

    private async Task FlushQualificationAsync()
    {
        try
        {
            // force:true performs a second generation if another synchronous qualification lands
            // after the first generation was captured but before its manifest commit completes.
            await _persistence.FlushAsync(force: true).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // SceneProgressCoordinator remains dirty after failure; normal autosave/shutdown retries.
            Log.Error("Achievements", $"Qualification save failed; progress remains dirty: {exception.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _flushRunning, 0);
        }
    }
}

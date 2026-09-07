using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Interaction;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Environment;
using DesktopBuddy.Interaction;
using DesktopBuddy.Objects;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Production adapter from live gameplay to the engine-independent achievement rules. It observes
/// existing semantic events and state only; no achievement changes damage, physics, economy or UI.
/// Platform dependencies are injected by the owning composition root rather than discovered from
/// arbitrary scene-tree paths.
/// </summary>
public partial class AchievementBootstrap : Node
{
    private const double PersistentEvaluationSeconds = 0.5;
    private const double SteamRetrySeconds = 5.0;
    private const float AirborneFloorClearancePixels = 3.0f;
    private const float BankShotWallTolerancePixels = 3.0f;
    private const string BackgroundPath = "user://environment/background.png";

    private readonly HashSet<int> _baseballsThatTouchedWall = [];
    private SandboxRoot _sandbox = null!;
    private BuddyProgressState _progress = null!;
    private SaveCoordinator _saves = null!;
    private WorkProgressState? _work;
    private EnvironmentProgressState? _environment;
    private CharacterSelectionState? _selection;
    private CharacterStore? _characters;
    private Node? _steamBridge;
    private AchievementCoordinator _coordinator = null!;
    private SteamAchievementPublisher? _publisher;
    private double _persistentCountdown;
    private double _steamCountdown;
    private double _airborneSeconds;
    private double _observedRunSeconds;
    private long _observedScoredImpacts;
    private bool _burnWasActive;
    private bool _characterRefreshRunning;
    private bool _steamSyncRequested;
    private int _observedRopeAttachCount;
    private string? _backgroundHash;

    public AchievementCoordinator Coordinator => _coordinator;

    public void Configure(
        SandboxRoot sandbox,
        CharacterSelectionState? selection = null,
        CharacterStore? characters = null,
        Node? initializedSteamBridge = null)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("AchievementBootstrap must be configured before entering the tree.");
        _sandbox = sandbox ?? throw new ArgumentNullException(nameof(sandbox));
        _progress = sandbox.Progress;
        _saves = sandbox.Saves;
        _work = sandbox.Saves.WorkProgress;
        _environment = sandbox.Saves.EnvironmentProgress;
        _selection = selection ?? sandbox.Saves.CharacterSelection;
        _characters = characters;
        _steamBridge = initializedSteamBridge;
        _coordinator = new AchievementCoordinator(_progress, _work);
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        if (_coordinator is null)
            throw new InvalidOperationException("AchievementBootstrap was not configured.");

        _sandbox.Pipeline.ImpactAccepted += OnImpactAccepted;
        _sandbox.Pipeline.CareMoodChanged += OnCareMoodChanged;
        _sandbox.Grab.Grabbed += OnGrabbed;
        _sandbox.FireSprayer.Ignited += OnIgnited;
        _sandbox.Buddy.ObjectInteraction.ConsumeSucceeded += OnCareItemTaken;
        _progress.Changed += OnProgressChanged;
        if (_work is not null)
            _work.Changed += OnWorkProgressChanged;
        if (_environment is not null)
            _environment.Changed += OnEnvironmentChanged;
        if (_characters is not null)
            _characters.LibraryChanged += OnCharacterLibraryChanged;
        if (_selection is not null)
            _selection.Changed += OnCharacterSelectionChanged;

        _observedRopeAttachCount = _sandbox.Ropes.AttachCount;
        CaptureProgressMonotonicState();
        // Existing room art predates this runtime observation. Make It Yours records which
        // character was active when a customization actually changes; merely selecting another
        // character beside an already-painted room must not transfer that credit.
        _backgroundHash = CurrentBackgroundHash();
        _coordinator.Store.Qualified += OnQualified;
        _coordinator.EvaluatePersistentState(_selection?.ActiveCharacterId);
        EvaluateEnvironment();
        _ = RefreshActiveCharacterAsync();

        _publisher = new SteamAchievementPublisher(
            _coordinator.Store,
            SteamAppIdentityResolver.Resolve(),
            _steamBridge);
        // Reconcile carried/offline/demo qualification once after the initial local rule pass.
        // Any qualifications raised during that pass are intentionally collapsed into this batch.
        _steamSyncRequested = false;
        _publisher.TrySynchronize();
    }

    public override void _ExitTree()
    {
        if (_coordinator is null)
            return;
        if (GodotObject.IsInstanceValid(_sandbox))
        {
            if (GodotObject.IsInstanceValid(_sandbox.Pipeline))
            {
                _sandbox.Pipeline.ImpactAccepted -= OnImpactAccepted;
                _sandbox.Pipeline.CareMoodChanged -= OnCareMoodChanged;
            }
            if (GodotObject.IsInstanceValid(_sandbox.Grab))
                _sandbox.Grab.Grabbed -= OnGrabbed;
            if (GodotObject.IsInstanceValid(_sandbox.FireSprayer))
                _sandbox.FireSprayer.Ignited -= OnIgnited;
            if (GodotObject.IsInstanceValid(_sandbox.Buddy) &&
                GodotObject.IsInstanceValid(_sandbox.Buddy.ObjectInteraction))
            {
                _sandbox.Buddy.ObjectInteraction.ConsumeSucceeded -= OnCareItemTaken;
            }
        }
        _progress.Changed -= OnProgressChanged;
        if (_work is not null)
            _work.Changed -= OnWorkProgressChanged;
        if (_environment is not null)
            _environment.Changed -= OnEnvironmentChanged;
        if (_characters is not null)
            _characters.LibraryChanged -= OnCharacterLibraryChanged;
        if (_selection is not null)
            _selection.Changed -= OnCharacterSelectionChanged;
        _coordinator.Store.Qualified -= OnQualified;
    }

    public override void _Process(double delta)
    {
        if (_coordinator is null || !GodotObject.IsInstanceValid(_sandbox))
            return;

        ObserveProgressDiscontinuity();
        ObserveAirborne(delta);
        ObserveBaseballWallTouches();
        ObserveRopeUse();
        if (!_sandbox.FireSprayer.IsBurning)
            _burnWasActive = false;

        _persistentCountdown -= delta;
        if (_persistentCountdown <= 0.0)
        {
            _persistentCountdown = PersistentEvaluationSeconds;
            ObserveBackgroundCustomization();
            _coordinator.EvaluatePersistentState(_selection?.ActiveCharacterId);
            EvaluateEnvironment();
            _ = RefreshActiveCharacterAsync();
        }

        // Qualification callbacks may arrive several times in one synchronous rule evaluation.
        // Collapse them into one desired-state reconciliation on the next process turn.
        if (_steamSyncRequested)
        {
            _steamSyncRequested = false;
            _publisher?.TrySynchronize();
        }

        _steamCountdown -= delta;
        if (_steamCountdown <= 0.0)
        {
            _steamCountdown = SteamRetrySeconds;
            _publisher?.TrySynchronize();
        }
    }

    private void OnImpactAccepted(AcceptedImpact impact)
    {
        _progress.RecordContentUse(impact.ContentId);
        _coordinator.RecordDamage(impact.ContentId, impact.Pain, impact.MilliCredits, impact.TimeSeconds);
        if (string.Equals(impact.ContentId, ContentIds.ToolBaseball, StringComparison.Ordinal) &&
            _baseballsThatTouchedWall.Remove(impact.InteractionId))
        {
            _coordinator.RecordBankShot();
        }
    }

    private void OnCareMoodChanged(CareKind kind, int _delta)
    {
        string? contentId = kind switch
        {
            CareKind.Pet => ContentIds.ToolPet,
            CareKind.Tickle => ContentIds.ToolTickle,
            _ => null,
        };
        if (contentId is not null)
            _progress.RecordContentUse(contentId);
    }

    private void OnGrabbed(RigidBody2D _target)
    {
        string selected = _progress.SelectedToolId;
        if (string.Equals(selected, ContentIds.ToolPowerGrab, StringComparison.Ordinal))
            _progress.RecordContentUse(ContentIds.ToolPowerGrab);
        else if (string.Equals(selected, ContentIds.ToolGrab, StringComparison.Ordinal))
            _progress.RecordContentUse(ContentIds.ToolGrab);
        // Rope Suspender gets its use only when a rope is actually attached, below.
    }

    private void ObserveRopeUse()
    {
        int current = _sandbox.Ropes.AttachCount;
        while (_observedRopeAttachCount < current)
        {
            _progress.RecordContentUse(ContentIds.ToolRopeSuspender);
            _observedRopeAttachCount++;
        }
        if (_observedRopeAttachCount > current)
            _observedRopeAttachCount = current;
    }

    private void OnIgnited(Vector2 _point)
    {
        _burnWasActive = true;
        _progress.RecordContentUse(ContentIds.ToolFireSprayer);
    }

    private void OnCareItemTaken(LooseObjectBody item)
    {
        if (GodotObject.IsInstanceValid(item) && !string.IsNullOrWhiteSpace(item.SemanticContentId))
            _progress.RecordContentUse(item.SemanticContentId);

        if (_burnWasActive && GodotObject.IsInstanceValid(item) &&
            GodotObject.IsInstanceValid(item.Profile) &&
            item.Profile!.ClearsHarmfulStatuses)
        {
            _coordinator.RecordFireDrill();
        }
        _burnWasActive = _sandbox.FireSprayer.IsBurning;
    }

    private void OnProgressChanged(ProgressChange _change) =>
        _coordinator.EvaluatePersistentState(_selection?.ActiveCharacterId);

    private void OnWorkProgressChanged() =>
        _coordinator.EvaluatePersistentState(_selection?.ActiveCharacterId);

    private void OnEnvironmentChanged()
    {
        EvaluateEnvironment();
        MarkEnvironmentCustomization();
    }

    private void OnCharacterLibraryChanged() => _ = RefreshActiveCharacterAsync();

    private void OnCharacterSelectionChanged(Guid? _id)
    {
        _coordinator.EvaluatePersistentState(_selection?.ActiveCharacterId);
        _ = RefreshActiveCharacterAsync();
    }

    private void OnQualified(AchievementDefinition definition)
    {
        GD.Print($"ACHIEVEMENT_QUALIFIED {definition.SteamApiName} ({definition.DisplayName})");
        _ = FlushQualificationAsync();
        _steamSyncRequested = true;
    }

    private async Task FlushQualificationAsync()
    {
        try
        {
            await _saves.FlushProgressAsync().ConfigureAwait(false);
        }
        catch
        {
            // SaveCoordinator retains the dirty revision and the normal autosave/quit path retries.
        }
    }

    private void ObserveProgressDiscontinuity()
    {
        ProgressStatistics statistics = _progress.Statistics;
        double runSeconds = _progress.Times.RunSeconds;
        bool rewound = statistics.ScoredImpacts < _observedScoredImpacts ||
                       runSeconds + 0.000001 < _observedRunSeconds;

        if (rewound)
        {
            _coordinator.ResetTransientObservations();
            _baseballsThatTouchedWall.Clear();
            _airborneSeconds = 0.0;
            _burnWasActive = false;
            _observedRopeAttachCount = _sandbox.Ropes.AttachCount;
        }

        _observedScoredImpacts = statistics.ScoredImpacts;
        _observedRunSeconds = runSeconds;
    }

    private void CaptureProgressMonotonicState()
    {
        _observedScoredImpacts = _progress.Statistics.ScoredImpacts;
        _observedRunSeconds = _progress.Times.RunSeconds;
    }

    private void ObserveAirborne(double delta)
    {
        if (_coordinator.Store.IsQualified(AchievementIds.AirBud))
            return;
        if (_sandbox.Grab.IsGrabbing || !_sandbox.Shell.GameplayInputEnabled ||
            _sandbox.Lifecycle.PauseCoordinator.IsPaused || _sandbox.Lifecycle.IsEditorModeActive)
        {
            _airborneSeconds = 0.0;
            return;
        }

        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        float floor = bounds.End.Y;
        bool clear = true;
        foreach (var part in _sandbox.Buddy.Rig.Parts)
        {
            if (part.GlobalPosition.Y + part.Radius >= floor - AirborneFloorClearancePixels)
            {
                clear = false;
                break;
            }
        }

        _airborneSeconds = clear ? _airborneSeconds + Math.Max(0.0, delta) : 0.0;
        _coordinator.RecordAirborneSeconds(_airborneSeconds);
    }

    private void ObserveBaseballWallTouches()
    {
        if (_coordinator.Store.IsQualified(AchievementIds.BankShot))
            return;
        Rect2 bounds = _sandbox.Boundaries.InnerBounds;
        for (int slot = 0; slot < LooseObjectRegistry.Capacity; slot++)
        {
            LooseObjectBody? body = _sandbox.Objects.BodyAt(slot);
            if (!GodotObject.IsInstanceValid(body) || body!.RuntimeId == 0 ||
                !string.Equals(body.SemanticContentId, ContentIds.ToolBaseball, StringComparison.Ordinal) ||
                !_sandbox.Objects.TryGetSnapshot(body.RuntimeId, out var snapshot) ||
                snapshot.ThrowToken == 0)
            {
                continue;
            }

            bool sideWall = body.GlobalPosition.X - body.Radius <= bounds.Position.X + BankShotWallTolerancePixels ||
                            body.GlobalPosition.X + body.Radius >= bounds.End.X - BankShotWallTolerancePixels;
            if (sideWall)
                _baseballsThatTouchedWall.Add(body.InteractionId);
        }
    }

    private void EvaluateEnvironment()
    {
        if (_environment is null)
            return;

        foreach (PlacedDecoration placed in _environment.Layout.Decorations)
        {
            EnvironmentDecorationResource? resource = EnvironmentDecorationRegistry.Find(placed.DefinitionId);
            if (GodotObject.IsInstanceValid(resource))
                _coordinator.RecordEnvironmentCategory(resource!.Category);
        }
    }

    private void MarkEnvironmentCustomization()
    {
        if (_environment?.Layout.Decorations.Count > 0)
            _coordinator.RecordCustomization(
                _selection?.ActiveCharacterId,
                CustomizationArea.EnvironmentDecorator);
    }

    private void ObserveBackgroundCustomization()
    {
        string? current = CurrentBackgroundHash();
        if (string.Equals(current, _backgroundHash, StringComparison.Ordinal))
            return;

        _backgroundHash = current;
        // Deletion is Reset Progress / explicit clearing, not customization. A new or changed
        // background file is a completed Paint Background save and belongs to the character active
        // at that moment; the resulting bit is persisted by AchievementProgressStore.
        if (!string.IsNullOrEmpty(current))
            _coordinator.RecordCustomization(
                _selection?.ActiveCharacterId,
                CustomizationArea.PaintBackground);
    }

    private static string? CurrentBackgroundHash()
    {
        if (!FileAccess.FileExists(BackgroundPath))
            return null;
        string hash = FileAccess.GetSha256(BackgroundPath);
        return string.IsNullOrEmpty(hash) ? null : hash;
    }

    private async Task RefreshActiveCharacterAsync()
    {
        if (_characterRefreshRunning || _characters is null ||
            _selection?.ActiveCharacterId is not Guid id || id == Guid.Empty)
        {
            return;
        }

        _characterRefreshRunning = true;
        try
        {
            CharacterLoadResult load = await _characters.LoadAsync(id, CancellationToken.None);
            if (load.Document is not CharacterDocument document)
                return;

            CharacterFeatureSet features = document.Features;
            _coordinator.RecordFullyDressed(
                !string.Equals(features.Headwear.FeatureId, CharacterFeatureIds.HeadwearNone, StringComparison.Ordinal),
                !string.Equals(features.Tops.FeatureId, CharacterFeatureIds.TopNone, StringComparison.Ordinal),
                !string.Equals(features.Shoes.FeatureId, CharacterFeatureIds.ShoesNone, StringComparison.Ordinal),
                !string.Equals(features.Glasses.FeatureId, CharacterFeatureIds.GlassesNone, StringComparison.Ordinal));

            // These two systems are intrinsic to the character document, so reconstructing their
            // credit on load is safe. Room/background systems are credited only from actual runtime
            // commits above; existing room state must never migrate onto a newly selected character.
            if (HasStudioCustomization(document))
                _coordinator.RecordCustomization(id, CustomizationArea.BuddyStudio);
            if (document.Paint.Declared().Any())
                _coordinator.RecordCustomization(id, CustomizationArea.PaintBuddy);
        }
        finally
        {
            _characterRefreshRunning = false;
        }
    }

    private static bool HasStudioCustomization(CharacterDocument document)
    {
        CharacterFeatureSet current = document.Features;
        CharacterFeatureSet defaults = CharacterFeatureSet.BuiltIn;
        return !string.Equals(current.Face.FeatureId, defaults.Face.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Hair.FeatureId, defaults.Hair.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Eyebrows.FeatureId, defaults.Eyebrows.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Eyes.FeatureId, defaults.Eyes.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Nose.FeatureId, defaults.Nose.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Mouth.FeatureId, defaults.Mouth.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Ears.FeatureId, defaults.Ears.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Accessories.FeatureId, defaults.Accessories.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Glasses.FeatureId, defaults.Glasses.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Headwear.FeatureId, defaults.Headwear.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Tops.FeatureId, defaults.Tops.FeatureId, StringComparison.Ordinal) ||
               !string.Equals(current.Shoes.FeatureId, defaults.Shoes.FeatureId, StringComparison.Ordinal);
    }
}

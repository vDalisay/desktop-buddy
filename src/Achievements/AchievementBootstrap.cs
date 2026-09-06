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
/// </summary>
public partial class AchievementBootstrap : Node
{
    private const double PersistentEvaluationSeconds = 0.5;
    private const double SteamRetrySeconds = 5.0;
    private const float AirborneFloorClearancePixels = 3.0f;
    private const float BankShotWallTolerancePixels = 3.0f;

    private readonly HashSet<int> _baseballsThatTouchedWall = [];
    private SandboxRoot _sandbox = null!;
    private BuddyProgressState _progress = null!;
    private SaveCoordinator _saves = null!;
    private WorkProgressState? _work;
    private EnvironmentProgressState? _environment;
    private CharacterSelectionState? _selection;
    private CharacterStore? _characters;
    private AchievementCoordinator _coordinator = null!;
    private SteamAchievementPublisher? _publisher;
    private double _persistentCountdown;
    private double _steamCountdown;
    private double _airborneSeconds;
    private bool _burnWasActive;
    private bool _characterRefreshRunning;

    public AchievementCoordinator Coordinator => _coordinator;

    public void Configure(
        SandboxRoot sandbox,
        CharacterSelectionState? selection = null,
        CharacterStore? characters = null)
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
        _coordinator = new AchievementCoordinator(_progress, _work);
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Ready()
    {
        if (_coordinator is null)
            throw new InvalidOperationException("AchievementBootstrap was not configured.");

        _sandbox.Pipeline.ImpactAccepted += OnImpactAccepted;
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

        _coordinator.Store.Qualified += OnQualified;
        _coordinator.EvaluatePersistentState(_selection?.ActiveCharacterId);
        EvaluateEnvironment();
        _ = RefreshActiveCharacterAsync();

        Node? bridge = GetTree().Root.FindChild("GodotSteamBridge", true, false) as Node;
        _publisher = new SteamAchievementPublisher(
            _coordinator.Store,
            SteamAppIdentityResolver.Resolve(),
            bridge);
        _publisher.TrySynchronize();
    }

    public override void _ExitTree()
    {
        if (_coordinator is null)
            return;
        if (GodotObject.IsInstanceValid(_sandbox))
        {
            if (GodotObject.IsInstanceValid(_sandbox.Pipeline))
                _sandbox.Pipeline.ImpactAccepted -= OnImpactAccepted;
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

        ObserveAirborne(delta);
        ObserveBaseballWallTouches();

        _persistentCountdown -= delta;
        if (_persistentCountdown <= 0.0)
        {
            _persistentCountdown = PersistentEvaluationSeconds;
            _coordinator.EvaluatePersistentState(_selection?.ActiveCharacterId);
            EvaluateEnvironment();
            _ = RefreshActiveCharacterAsync();
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
        _coordinator.RecordDamage(impact.ContentId, impact.Pain, impact.MilliCredits, impact.TimeSeconds);
        if (string.Equals(impact.ContentId, ContentIds.ToolBaseball, StringComparison.Ordinal) &&
            _baseballsThatTouchedWall.Remove(impact.InteractionId))
        {
            _coordinator.RecordBankShot();
        }
    }

    private void OnIgnited(Vector2 _point) => _burnWasActive = true;

    private void OnCareItemTaken(LooseObjectBody item)
    {
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
        _publisher?.TrySynchronize();
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

    private void ObserveAirborne(double delta)
    {
        if (_coordinator.Store.IsQualified(AchievementIds.AirBud))
            return;
        if (_sandbox.Grab.IsGrabbing)
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
                _coordinator.RecordEnvironmentCategory(resource!.Category.ToString());
        }
        _coordinator.EvaluateHomeSweetHome(Enum.GetNames<DecorationCategory>());
    }

    private void MarkEnvironmentCustomization()
    {
        if (_environment?.Layout.Decorations.Count > 0)
            _coordinator.RecordCustomization(
                _selection?.ActiveCharacterId,
                CustomizationArea.EnvironmentDecorator);
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

            if (HasStudioCustomization(document))
                _coordinator.RecordCustomization(id, CustomizationArea.BuddyStudio);
            if (document.Paint.Declared().Any())
                _coordinator.RecordCustomization(id, CustomizationArea.PaintBuddy);
            if (FileAccess.FileExists(
                    $"user://{SteamCloudSavePolicy.EnvironmentDirectoryName}/{SteamCloudSavePolicy.EnvironmentBackgroundFileName}"))
            {
                _coordinator.RecordCustomization(id, CustomizationArea.PaintBackground);
            }
            if (_environment?.Layout.Decorations.Count > 0)
                _coordinator.RecordCustomization(id, CustomizationArea.EnvironmentDecorator);
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

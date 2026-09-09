using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Achievements;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Environment;
using DesktopBuddy.Persistence.Characters;
using Godot;

namespace DesktopBuddy.Achievements;

/// <summary>
/// Persisted customization observers for the three baseline achievements that cannot be inferred
/// from combat/Work statistics. Character-derived checks read the Character document bound to the
/// current primary Buddy identity; room-derived checks read the active Scene document. Paint
/// Background remains event-driven so Reset Progress, unchanged saves, and Workshop room imports do
/// not impersonate an authored paint commit.
/// </summary>
public sealed partial class AchievementBootstrap
{
    private const double CustomizationEvaluationSeconds = 0.5;

    private CharacterStore? _characterDocuments;
    private IEnvironmentCustomizationEvents? _environmentEvents;
    private bool _customizationObserversWired;
    private bool _characterRefreshRunning;
    private volatile bool _characterRefreshRequested = true;
    private Guid? _observedCharacterId;
    private SceneId _observedEnvironmentSceneId;
    private long _observedEnvironmentRevision = long.MinValue;
    private double _customizationEvaluationCountdown;

    private void WireCustomizationObservers()
    {
        if (_customizationObserversWired)
            return;

        _characterDocuments = _sandbox.CharacterDocuments;
        if (_characterDocuments is not null)
            _characterDocuments.LibraryChanged += OnAchievementCharacterLibraryChanged;

        // This is one named composition peer, not a persistence/service lookup. The environment
        // bootstrap exposes only its semantic commit port; achievement code never reaches its UI.
        _environmentEvents = GetNodeOrNull<EnvironmentCustomizationBootstrap>(
            "/root/EnvironmentCustomizationBootstrap");
        if (_environmentEvents is not null)
            _environmentEvents.BackgroundCommitted += OnAchievementBackgroundCommitted;

        _customizationObserversWired = true;
        _customizationEvaluationCountdown = 0.0;
        EvaluatePersistedCustomizationState();
    }

    private void TickCustomizationObservers(double delta)
    {
        if (!_customizationObserversWired)
            return;

        _customizationEvaluationCountdown -= Math.Max(0.0, delta);
        if (_customizationEvaluationCountdown > 0.0)
            return;

        _customizationEvaluationCountdown = CustomizationEvaluationSeconds;
        EvaluatePersistedCustomizationState();
    }

    private void EvaluatePersistedCustomizationState()
    {
        EvaluateActiveSceneDecorations();

        Guid? characterId = null;
        if (_scenes.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary) &&
            primary is not null)
        {
            characterId = primary.CharacterId;
        }

        if (characterId != _observedCharacterId)
        {
            _observedCharacterId = characterId;
            _characterRefreshRequested = true;
        }

        if (_characterRefreshRequested && !_characterRefreshRunning)
            _ = RefreshPrimaryCharacterAchievementsAsync();
    }

    private void EvaluateActiveSceneDecorations()
    {
        EnvironmentProgressSnapshot environment = _scenes.ActiveEnvironmentProgress;
        SceneId sceneId = _scenes.ActiveSceneId;
        if (sceneId == _observedEnvironmentSceneId &&
            environment.Revision == _observedEnvironmentRevision)
        {
            return;
        }

        _observedEnvironmentSceneId = sceneId;
        _observedEnvironmentRevision = environment.Revision;

        foreach (PlacedDecoration placed in environment.Layout.Decorations)
        {
            EnvironmentDecorationResource? resource =
                EnvironmentDecorationRegistry.Find(placed.DefinitionId);
            if (GodotObject.IsInstanceValid(resource))
                _coordinator.RecordEnvironmentCategory(resource!.Category);
        }

        if (environment.Layout.Decorations.Count > 0 &&
            _scenes.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary) &&
            primary is not null)
        {
            _coordinator.RecordCustomization(
                primary.BuddyIdentityId,
                CustomizationArea.EnvironmentDecorator);
        }
    }

    private void OnAchievementBackgroundCommitted()
    {
        if (_scenes.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary) &&
            primary is not null)
        {
            _coordinator.RecordCustomization(
                primary.BuddyIdentityId,
                CustomizationArea.PaintBackground);
        }
    }

    private void OnAchievementCharacterLibraryChanged() => _characterRefreshRequested = true;

    private async Task RefreshPrimaryCharacterAchievementsAsync()
    {
        CharacterStore? documents = _characterDocuments;
        if (documents is null ||
            !_scenes.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary) ||
            primary is null)
        {
            _characterRefreshRequested = false;
            return;
        }

        Guid? requestedCharacterId = primary.CharacterId;
        if (requestedCharacterId is not Guid characterId || characterId == Guid.Empty)
        {
            _characterRefreshRequested = false;
            return;
        }

        _characterRefreshRunning = true;
        _characterRefreshRequested = false;
        try
        {
            CharacterLoadResult load = await documents.LoadAsync(characterId, CancellationToken.None);
            if (load.Document is not CharacterDocument document)
                return;

            // Do not apply a completed asynchronous read to a Buddy that switched Character while
            // the file was loading.
            if (!_scenes.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? current) ||
                current is null || current.CharacterId != requestedCharacterId)
            {
                _characterRefreshRequested = true;
                return;
            }

            CharacterFeatureSet features = document.Features;
            _coordinator.RecordFullyDressed(
                !string.Equals(features.Headwear.FeatureId, CharacterFeatureIds.HeadwearNone, StringComparison.Ordinal),
                !string.Equals(features.Tops.FeatureId, CharacterFeatureIds.TopNone, StringComparison.Ordinal),
                !string.Equals(features.Shoes.FeatureId, CharacterFeatureIds.ShoesNone, StringComparison.Ordinal),
                !string.Equals(features.Glasses.FeatureId, CharacterFeatureIds.GlassesNone, StringComparison.Ordinal));

            // Buddy Studio and Paint Buddy are intrinsic to the persisted Character document, so
            // they can be reconstructed safely after restart. Room/background credit is handled by
            // the Scene/event paths above and is never copied merely because a Character is loaded.
            if (HasStudioCustomization(document))
            {
                _coordinator.RecordCustomization(
                    current.BuddyIdentityId,
                    CustomizationArea.BuddyStudio);
            }

            if (document.Paint.Declared().Any())
            {
                _coordinator.RecordCustomization(
                    current.BuddyIdentityId,
                    CustomizationArea.PaintBuddy);
            }
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

    private void UnwireCustomizationObservers()
    {
        if (!_customizationObserversWired)
            return;

        if (_characterDocuments is not null)
            _characterDocuments.LibraryChanged -= OnAchievementCharacterLibraryChanged;
        if (_environmentEvents is not null)
            _environmentEvents.BackgroundCommitted -= OnAchievementBackgroundCommitted;

        _characterDocuments = null;
        _environmentEvents = null;
        _characterRefreshRunning = false;
        _characterRefreshRequested = true;
        _observedCharacterId = null;
        _observedEnvironmentSceneId = default;
        _observedEnvironmentRevision = long.MinValue;
        _customizationObserversWired = false;
    }
}

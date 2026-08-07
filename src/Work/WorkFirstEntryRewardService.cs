using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.Work;

public readonly record struct WorkFirstEntryRewardResult(
    bool WasFirstEntry,
    bool OwnershipGranted,
    bool CharacterEquipped,
    string? Detail = null);

/// <summary>
/// One-shot bridge between Work Mode and the shared cosmetic ownership/character document
/// systems. It never invents Work-only inventory state.
/// </summary>
public sealed class WorkFirstEntryRewardService
{
    private const string Category = "WorkMode";

    private readonly BuddyProgressState _progress;
    private readonly WorkProgressState _work;
    private readonly CharacterSelectionState? _selection;
    private readonly CharacterStore? _characters;
    private readonly SaveCoordinator _saves;

    public WorkFirstEntryRewardService(
        BuddyProgressState progress,
        WorkProgressState work,
        CharacterSelectionState? selection,
        CharacterStore? characters,
        SaveCoordinator saves)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _selection = selection;
        _characters = characters;
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
    }

    public async Task<WorkFirstEntryRewardResult> EnsureAsync(CancellationToken token = default)
    {
        if (_work.FirstEntryGlassesGranted)
            return new WorkFirstEntryRewardResult(false, false, false);

        bool ownershipGranted = _progress.Unlock(ContentIds.CosmeticWorkGlasses);
        bool equipped = false;
        Guid? activeId = _selection?.ActiveCharacterId;

        if (activeId.HasValue && _characters is not null)
        {
            CharacterLoadResult loaded = await _characters.LoadAsync(activeId.Value, token).ConfigureAwait(false);
            if (loaded.Document is null)
            {
                return new WorkFirstEntryRewardResult(
                    true,
                    ownershipGranted,
                    false,
                    $"Work glasses owned, but active character could not be loaded: {loaded.Status}.");
            }

            CharacterDocument updated = loaded.Document with
            {
                Features = loaded.Document.Features with
                {
                    Glasses = loaded.Document.Features.Glasses with
                    {
                        FeatureId = CharacterFeatureIds.GlassesWorkClassic,
                    },
                },
            };
            CharacterSaveResult saved = await _characters.SaveAsync(updated, token).ConfigureAwait(false);
            if (saved.Status != CharacterSaveStatus.Saved)
            {
                return new WorkFirstEntryRewardResult(
                    true,
                    ownershipGranted,
                    false,
                    $"Work glasses owned, but auto-equip save failed: {saved.Status}.");
            }
            equipped = true;
        }

        _work.MarkFirstEntryGlassesGranted();
        try
        {
            await _saves.FlushProgressAsync(force: true, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.Error(Category, $"First-entry Work reward progress save failed: {exception.Message}");
            throw;
        }

        return new WorkFirstEntryRewardResult(true, ownershipGranted, equipped);
    }
}

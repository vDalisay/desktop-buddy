using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;

namespace DesktopBuddy.Work;

public readonly record struct WorkFirstEntryRewardResult(
    bool WasFirstEntry,
    bool OwnershipGranted);

/// <summary>
/// One-shot bridge between Work Mode and shared cosmetic ownership. It never invents
/// Work-only inventory state or changes the active character's equipment.
/// </summary>
public sealed class WorkFirstEntryRewardService
{
    private readonly PlayerRuntimeProgressBinding _progress;
    private readonly WorkProgressState _work;
    private readonly IRunProgressPersistence _saves;

    public WorkFirstEntryRewardService(
        BuddyProgressState progress,
        WorkProgressState work,
        SaveCoordinator saves)
        : this(
            new PlayerRuntimeProgressBinding(progress),
            work,
            new LegacyRunProgressPersistence(saves))
    {
    }

    public WorkFirstEntryRewardService(
        PlayerRuntimeProgressBinding progress,
        WorkProgressState work,
        IRunProgressPersistence saves)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _work = work ?? throw new ArgumentNullException(nameof(work));
        _saves = saves ?? throw new ArgumentNullException(nameof(saves));
    }

    public async Task<WorkFirstEntryRewardResult> EnsureAsync(CancellationToken token = default)
    {
        if (_work.FirstEntryGlassesGranted)
            return new WorkFirstEntryRewardResult(false, false);

        bool ownershipGranted = _progress.Unlock(ContentIds.CosmeticWorkGlasses);
        _work.MarkFirstEntryGlassesGranted();
        await _saves.FlushAsync(force: true, token).ConfigureAwait(false);

        return new WorkFirstEntryRewardResult(true, ownershipGranted);
    }
}

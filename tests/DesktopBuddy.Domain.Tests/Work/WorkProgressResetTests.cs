using System.IO;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Work;

public sealed class WorkProgressResetTests
{
    [Fact]
    public async Task ResetProgress_ClearsWorkCountersClaimsAndFirstEntryFlag()
    {
        var progress = new BuddyProgressState(0.018);
        var work = new WorkProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, work: work);

        work.Record(WorkActivityKind.KeyboardPress, 12_345);
        work.Record(WorkActivityKind.MouseClick, 678);
        Assert.True(work.ClaimLifetimeMilestone("work.test.claim"));
        Assert.True(work.MarkFirstEntryGlassesGranted());

        Assert.True(await ProgressReset.ResetAsync(progress, saves));

        Assert.Equal(0, work.Lifetime.KeyboardPresses);
        Assert.Equal(0, work.Lifetime.MouseClicks);
        Assert.Empty(work.ClaimedLifetimeMilestoneIds);
        Assert.False(work.FirstEntryGlassesGranted);
        Assert.False(saves.IsDirty);
    }

    [Fact]
    public async Task FailedResetWrite_RestoresExactWorkSnapshot()
    {
        var progress = new BuddyProgressState(0.018);
        var work = new WorkProgressState();
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store, work: work);

        work.Record(WorkActivityKind.KeyboardPress, 42);
        work.Record(WorkActivityKind.MouseClick, 9);
        work.ClaimLifetimeMilestone("work.test.claim");
        work.MarkFirstEntryGlassesGranted();
        await saves.FlushProgressAsync(force: true);
        WorkProgressSnapshot before = work.Snapshot();

        store.NextProgressFailure = new IOException("Injected reset failure.");
        Assert.False(await ProgressReset.ResetAsync(progress, saves));

        WorkProgressSnapshot after = work.Snapshot();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Lifetime, after.Lifetime);
        Assert.Equal(before.FirstEntryGlassesGranted, after.FirstEntryGlassesGranted);
        Assert.Equal(before.ClaimedLifetimeMilestoneIds, after.ClaimedLifetimeMilestoneIds);
        Assert.False(saves.IsDirty);
    }
}

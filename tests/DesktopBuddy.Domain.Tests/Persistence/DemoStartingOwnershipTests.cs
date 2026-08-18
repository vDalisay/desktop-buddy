using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

/// <summary>
/// Pins the Demo ownership transition separately from the broader save-policy matrix: new saves
/// receive only Grab, while an existing save keeps every tool it already owned when the default
/// starter set changes underneath it.
/// </summary>
public sealed class DemoStartingOwnershipTests
{
    private const double CashPerPain = 0.5;

    [Fact]
    public void FreshDemoSaveOwnsOnlyGrab()
    {
        var state = new BuddyProgressState(CashPerPain);

        Assert.Equal(ToolId.Grab, state.SelectedTool);
        Assert.True(state.IsToolUnlocked(ContentIds.ToolGrab));
        Assert.False(state.IsToolUnlocked(ContentIds.ToolPet));
        Assert.False(state.IsToolUnlocked(ContentIds.ToolTickle));
        Assert.False(state.IsToolUnlocked(ContentIds.ToolBoxingGlove));
    }

    [Fact]
    public void ExistingSaveKeepsFormerStarterToolsAfterDefaultChanges()
    {
        var save = new ProgressSave
        {
            UnlockedToolIds =
            [
                ContentIds.ToolGrab,
                ContentIds.ToolPet,
                ContentIds.ToolTickle,
                ContentIds.ToolBoxingGlove,
            ],
            SelectedToolId = ContentIds.ToolTickle,
        };

        BuddyProgressState state = ProgressSavePolicy.CreateState(save, CashPerPain);
        ProgressSave roundTrip = ProgressSave.FromSnapshot(state.Snapshot());

        Assert.Equal(ToolId.Tickle, state.SelectedTool);
        Assert.True(state.IsToolUnlocked(ContentIds.ToolGrab));
        Assert.True(state.IsToolUnlocked(ContentIds.ToolPet));
        Assert.True(state.IsToolUnlocked(ContentIds.ToolTickle));
        Assert.True(state.IsToolUnlocked(ContentIds.ToolBoxingGlove));
        Assert.Contains(ContentIds.ToolPet, roundTrip.UnlockedToolIds);
        Assert.Contains(ContentIds.ToolTickle, roundTrip.UnlockedToolIds);
        Assert.Contains(ContentIds.ToolBoxingGlove, roundTrip.UnlockedToolIds);
    }
}

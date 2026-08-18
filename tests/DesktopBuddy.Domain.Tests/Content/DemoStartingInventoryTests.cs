using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tests.Content;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Content;

/// <summary>Demo-specific ownership contract kept separate from the older M5 fixtures.</summary>
public sealed class DemoStartingInventoryTests
{
    [Fact]
    public void FreshSaveOwnsOnlyNormalGrab()
    {
        var progress = new BuddyProgressState(cashPerPain: 1.0);

        Assert.Equal(new[] { ContentIds.ToolGrab }, CataloguePolicy.NewSaveUnlockedContentIds);
        Assert.Equal(ToolId.Grab, progress.SelectedTool);
        Assert.True(progress.IsToolUnlocked(ContentIds.ToolGrab));
        Assert.All(
            CataloguePolicy.LaunchContentIds.Where(id => id != ContentIds.ToolGrab),
            id => Assert.False(progress.IsToolUnlocked(id), id));
    }

    [Fact]
    public void PetTickleAndBoxingGloveAreVisiblePurchases()
    {
        ToolCatalogue catalogue = TestCatalogues.AllVisible();
        string[] shop = CataloguePolicy.ShopEntries(catalogue)
            .Select(entry => entry.ContentId)
            .ToArray();

        Assert.Contains(ContentIds.ToolPet, shop);
        Assert.Contains(ContentIds.ToolTickle, shop);
        Assert.Contains(ContentIds.ToolBoxingGlove, shop);
        Assert.DoesNotContain(ContentIds.ToolGrab, shop);
    }

    [Fact]
    public void DevelopmentLabMayUnlockToolsWithoutChangingFreshSavePolicy()
    {
        var progress = new BuddyProgressState(cashPerPain: 1.0);

        Assert.True(progress.Unlock(ContentIds.ToolPet));
        Assert.True(progress.SelectTool(ToolId.Pet));
        Assert.Equal(ToolId.Pet, progress.SelectedTool);

        var secondFreshSave = new BuddyProgressState(cashPerPain: 1.0);
        Assert.False(secondFreshSave.IsToolUnlocked(ContentIds.ToolPet));
        Assert.Equal(ToolId.Grab, secondFreshSave.SelectedTool);
    }
}

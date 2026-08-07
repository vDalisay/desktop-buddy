using DesktopBuddy.Domain.Content;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Content;

public sealed class CosmeticCatalogueTests
{
    [Fact]
    public void CosmeticEntry_IsValidButNeverToolSelectable()
    {
        var entry = new CatalogueEntry(
            ContentIds.CosmeticWorkGlasses,
            CatalogueEntryKind.Cosmetic,
            1_000,
            10_000,
            true,
            "cosmetic.work_glasses.name",
            "cosmetic.work_glasses.description");

        var catalogue = new ToolCatalogue([entry]);

        Assert.True(catalogue.Contains(ContentIds.CosmeticWorkGlasses));
        Assert.False(entry.IsSelectable);
        Assert.True(ContentIds.IsCosmetic(ContentIds.CosmeticWorkGlasses));
    }
}

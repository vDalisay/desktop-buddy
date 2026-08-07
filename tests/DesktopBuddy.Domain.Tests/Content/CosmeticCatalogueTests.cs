using DesktopBuddy.Domain.Content;
using FluentAssertions;
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

        catalogue.Contains(ContentIds.CosmeticWorkGlasses).Should().BeTrue();
        entry.IsSelectable.Should().BeFalse();
        ContentIds.IsCosmetic(ContentIds.CosmeticWorkGlasses).Should().BeTrue();
    }
}

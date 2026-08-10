using System.Linq;
using DesktopBuddy.Domain.Characters;
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

    [Fact]
    public void ReleasedCosmeticsAreStudioOnlyAndDoNotChangeTheToolSchedule()
    {
        ToolCatalogue tools = TestCatalogues.AllVisible();
        var cosmetic = new CatalogueEntry(
            ContentIds.CosmeticHairShortSweep,
            CatalogueEntryKind.Cosmetic,
            4_000,
            16,
            true,
            "cosmetic.hair.short_sweep.name",
            "cosmetic.hair.short_sweep.description");
        var catalogue = new ToolCatalogue([.. tools.Entries, cosmetic]);

        Assert.Empty(CataloguePolicy.ValidateLaunchCatalogue(catalogue));
        Assert.DoesNotContain(CataloguePolicy.ShopEntries(catalogue), entry => entry.ContentId == cosmetic.ContentId);
        Assert.DoesNotContain(CataloguePolicy.SelectableEntries(catalogue), entry => entry.ContentId == cosmetic.ContentId);
        Assert.Equal([cosmetic], CataloguePolicy.CosmeticEntries(catalogue));
        Assert.Equal(CataloguePolicy.LaunchContentIds, CataloguePolicy.SelectableEntries(catalogue).Select(entry => entry.ContentId));
    }

    [Fact]
    public void SaleDefinitionsUseTheirExactAuthoredOwnershipIds()
    {
        (string FeatureId, string ContentId)[] mappings =
        [
            (CharacterFeatureIds.HairShortSweep, ContentIds.CosmeticHairShortSweep),
            (CharacterFeatureIds.NoseButton, ContentIds.CosmeticNoseButton),
            (CharacterFeatureIds.EarsRoundTabs, ContentIds.CosmeticEarsRoundTabs),
            (CharacterFeatureIds.HeadwearSoftCap, ContentIds.CosmeticHeadwearSoftCap),
            (CharacterFeatureIds.TopUtilityBib, ContentIds.CosmeticTopUtilityBib),
            (CharacterFeatureIds.ShoesSoftSteps, ContentIds.CosmeticShoesSoftSteps),
        ];

        Assert.All(mappings, mapping =>
        {
            Assert.True(CharacterFeatureCatalog.Shipped.TryGetDefinition(mapping.FeatureId, out CosmeticDefinition definition));
            Assert.Equal(mapping.ContentId, definition.OwnershipContentId);
        });
        Assert.Equal(ContentIds.CosmeticWorkGlasses,
            CharacterFeatureCatalog.Shipped.ResolveDefinition(
                CharacterFeatureSlot.Glasses, CharacterFeatureIds.GlassesWorkClassic, out _).OwnershipContentId);
    }
}

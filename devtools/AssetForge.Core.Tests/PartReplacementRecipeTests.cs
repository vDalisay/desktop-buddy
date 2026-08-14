using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class PartReplacementRecipeTests
{
    [Fact]
    public void Replacement_defaults_are_valid_and_category_specific()
    {
        AssetRecipe torso = AssetRecipe.TorsoShapeDefaults();
        AssetRecipe feet = AssetRecipe.FootShapeDefaults();
        Assert.Empty(torso.Validate());
        Assert.Empty(feet.Validate());
        Assert.Equal(AssetCategory.TorsoShape, torso.Category);
        Assert.Equal(AssetCategory.FootShape, feet.Category);
        Assert.Equal(ShapeMode.InflatedSolid, torso.Geometry.ShapeMode);
        Assert.Equal(ShapeMode.InflatedSolid, feet.Geometry.ShapeMode);
    }

    [Fact]
    public void Replacement_templates_are_deterministic_1024_rgba()
    {
        byte[] torso = AuthoringTemplateCatalog.CreatePng(AuthoringTemplateCatalog.TorsoId);
        byte[] feet = AuthoringTemplateCatalog.CreatePng(AuthoringTemplateCatalog.FeetId);
        AssertTemplate(torso);
        AssertTemplate(feet);
        Assert.Equal(Hashing.Sha256Hex(torso), Hashing.Sha256Hex(AuthoringTemplateCatalog.CreatePng(AuthoringTemplateCatalog.TorsoId)));
    }

    private static void AssertTemplate(byte[] png)
    {
        RgbaImage image = PngCodec.DecodeRgba8(png);
        Assert.Equal(1024, image.Width);
        Assert.Equal(1024, image.Height);
        Assert.Contains(image.Pixels.Where((_, index) => index % 4 == 3), alpha => alpha is > 0 and < 255);
    }
}

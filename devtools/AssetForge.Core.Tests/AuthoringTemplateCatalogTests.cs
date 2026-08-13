using DesktopBuddy.AssetForge.Core;
using Xunit;

namespace DesktopBuddy.AssetForge.Core.Tests;

public sealed class AuthoringTemplateCatalogTests
{
    [Fact]
    public void Category_template_contracts_have_unique_stable_ids_and_filenames()
    {
        Assert.NotEmpty(AuthoringTemplateCatalog.All);
        Assert.Equal(
            AuthoringTemplateCatalog.All.Count,
            AuthoringTemplateCatalog.All.Select(static spec => spec.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            AuthoringTemplateCatalog.All.Count,
            AuthoringTemplateCatalog.All.Select(static spec => spec.TemplateFileName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(AuthoringTemplateCatalog.All, static spec =>
        {
            Assert.False(string.IsNullOrWhiteSpace(spec.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(spec.Summary));
            Assert.NotEmpty(spec.Guides);
            Assert.EndsWith("_1024.png", spec.TemplateFileName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Every_implemented_template_generates_deterministic_1024_rgba_output()
    {
        foreach (AuthoringTemplateSpec spec in AuthoringTemplateCatalog.All.Where(static spec => spec.Implemented))
        {
            byte[] first = AuthoringTemplateCatalog.CreatePng(spec.Id);
            byte[] second = AuthoringTemplateCatalog.CreatePng(spec.Id);
            Assert.Equal(first, second);

            RgbaImage decoded = PngCodec.DecodeRgba8(first);
            Assert.Equal(1024, decoded.Width);
            Assert.Equal(1024, decoded.Height);
        }
    }

    [Fact]
    public void Planned_template_cannot_be_generated_before_its_vertical_slice_exists()
    {
        AuthoringTemplateSpec planned = AuthoringTemplateCatalog.All.First(static spec => !spec.Implemented);
        Assert.Throws<NotSupportedException>(() => AuthoringTemplateCatalog.CreatePng(planned.Id));
    }
}

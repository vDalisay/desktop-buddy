using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Tools;

public sealed class ToolSelectionTests
{
    [Fact]
    public void NewSelection_DefaultsToGrab() =>
        Assert.Equal(ToolId.Grab, new ToolSelection().Selected);

    [Fact]
    public void Select_ChangesSelectedTool()
    {
        var selection = new ToolSelection();

        selection.Select(ToolId.BoxingGlove);

        Assert.Equal(ToolId.BoxingGlove, selection.Selected);
    }

    [Theory]
    [InlineData(ToolId.Grab, ToolCategory.Grab)]
    [InlineData(ToolId.Pet, ToolCategory.Care)]
    [InlineData(ToolId.Tickle, ToolCategory.Care)]
    [InlineData(ToolId.BoxingGlove, ToolCategory.Damage)]
    public void CategoryOf_ClassifiesTools(ToolId tool, ToolCategory expected) =>
        Assert.Equal(expected, ToolCatalog.CategoryOf(tool));

    [Theory]
    [InlineData(ToolId.Pet, CareKind.Pet)]
    [InlineData(ToolId.Tickle, CareKind.Tickle)]
    public void CareKindOf_MapsCareTools(ToolId tool, CareKind expected) =>
        Assert.Equal(expected, ToolCatalog.CareKindOf(tool));

    [Theory]
    [InlineData(ToolId.Grab)]
    [InlineData(ToolId.BoxingGlove)]
    public void CareKindOf_NonCareTools_IsNull(ToolId tool) =>
        Assert.Null(ToolCatalog.CareKindOf(tool));
}

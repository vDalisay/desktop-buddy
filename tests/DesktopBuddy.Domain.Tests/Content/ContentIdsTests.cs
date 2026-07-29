using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Content;

public sealed class ContentIdsTests
{
    [Theory]
    [InlineData(ToolId.Grab, "tool.grab")]
    [InlineData(ToolId.Pet, "tool.pet")]
    [InlineData(ToolId.Tickle, "tool.tickle")]
    [InlineData(ToolId.BoxingGlove, "tool.boxing_glove")]
    [InlineData(ToolId.Baseball, "tool.baseball")]
    public void ForTool_MapsToTheShippedOrdinalString(ToolId tool, string expected) =>
        Assert.Equal(expected, ContentIds.ForTool(tool));

    [Fact]
    public void ForTool_IsTotalOverTheEnum()
    {
        // A new ToolId member without a ContentIds entry must fail loudly, never fall
        // back to a wrong ID that would then be written into a save.
        foreach (ToolId tool in Enum.GetValues<ToolId>())
        {
            Assert.False(string.IsNullOrWhiteSpace(ContentIds.ForTool(tool)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => ContentIds.ForTool((ToolId)999));
    }

    [Fact]
    public void ToolIds_AreUniqueAndDoNotCollideWithNonToolContent()
    {
        var all = new HashSet<string>(StringComparer.Ordinal);

        foreach (ToolId tool in Enum.GetValues<ToolId>())
        {
            Assert.True(all.Add(ContentIds.ForTool(tool)), $"duplicate ID for {tool}");
        }

        Assert.True(all.Add(ContentIds.LooseObject));
        Assert.True(all.Add(ContentIds.RoomBoundary));
        Assert.True(all.Add(ContentIds.CareLabFood));
    }

    [Fact]
    public void RoundTrip_RestoresEveryTool()
    {
        foreach (ToolId tool in Enum.GetValues<ToolId>())
        {
            Assert.True(ContentIds.TryParseTool(ContentIds.ForTool(tool), out ToolId parsed));
            Assert.Equal(tool, parsed);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tool.grenade")] // from a newer build
    [InlineData("object.loose")]
    [InlineData("Tool.Grab")] // ordinal comparison: case matters
    public void TryParseTool_UnknownIdFallsBackToGrabWithoutClaimingSuccess(string? contentId)
    {
        Assert.False(ContentIds.TryParseTool(contentId, out ToolId tool));

        // FR-015.1: an unknown selected tool resolves to the safe default. The caller
        // keeps the original string so unknown data survives a load/save round-trip.
        Assert.Equal(ToolSelection.DefaultTool, tool);
        Assert.False(ContentIds.IsTool(contentId));
    }
}

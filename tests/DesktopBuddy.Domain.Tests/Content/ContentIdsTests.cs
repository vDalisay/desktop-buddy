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
    [InlineData(ToolId.Meal, "tool.meal")]
    [InlineData(ToolId.BaseballBat, "tool.baseball_bat")]
    [InlineData(ToolId.NerfBlaster, "tool.nerf_blaster")]
    [InlineData(ToolId.Pistol, "tool.pistol")]
    [InlineData(ToolId.Grenade, "tool.grenade")]
    [InlineData(ToolId.FireSprayer, "tool.fire_sprayer")]
    [InlineData(ToolId.SoccerBall, "tool.soccer_ball")]
    [InlineData(ToolId.Drink, "tool.drink")]
    [InlineData(ToolId.Shotgun, "tool.shotgun")]
    [InlineData(ToolId.RepairKit, "tool.repair_kit")]
    [InlineData(ToolId.PowerGrab, "tool.power_grab")]
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
        Assert.True(all.Add(ContentIds.UpgradeStrength));
    }

    [Fact]
    public void EveryToolOrdinalKeepsItsShippedValue()
    {
        // Ordinals are persisted in legacy integer saves: append only, never renumber.
        Assert.Equal(0, (int)ToolId.Grab);
        Assert.Equal(1, (int)ToolId.Pet);
        Assert.Equal(2, (int)ToolId.Tickle);
        Assert.Equal(3, (int)ToolId.BoxingGlove);
        Assert.Equal(4, (int)ToolId.Baseball);
        Assert.Equal(5, (int)ToolId.Meal);
        Assert.Equal(6, (int)ToolId.BaseballBat);
        Assert.Equal(7, (int)ToolId.Pistol);
        Assert.Equal(8, (int)ToolId.Grenade);
        Assert.Equal(9, (int)ToolId.FireSprayer);
        Assert.Equal(10, (int)ToolId.SoccerBall);
        Assert.Equal(11, (int)ToolId.Drink);
        Assert.Equal(12, (int)ToolId.Shotgun);
        Assert.Equal(13, (int)ToolId.RepairKit);
        Assert.Equal(14, (int)ToolId.NerfBlaster);
        Assert.Equal(15, (int)ToolId.PowerGrab);
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
    [InlineData("tool.from_a_later_build")] // from a newer build
    [InlineData("upgrade.strength")] // catalogue content, deliberately never a tool (FR-019)
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

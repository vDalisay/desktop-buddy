using System;
using System.Linq;
using DesktopBuddy.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class SteamCloudSavePolicyTests
{
    private static readonly string CharacterId = Guid.Parse("11111111-2222-3333-4444-555555555555").ToString("N");

    [Theory]
    [InlineData("progress.json")]
    [InlineData("environment/background.png")]
    public void Canonical_root_files_are_cloud_eligible(string relativePath) =>
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(relativePath));

    [Theory]
    [InlineData("head.png")]
    [InlineData("torso.png")]
    [InlineData("left_hand.png")]
    [InlineData("right_hand.png")]
    [InlineData("left_foot.png")]
    [InlineData("right_foot.png")]
    public void Canonical_character_files_are_cloud_eligible(string paintFile)
    {
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"characters/{CharacterId}/character.json"));
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"characters/{CharacterId}/paint/{paintFile}"));
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("progress.json.bak")]
    [InlineData("progress.json.tmp")]
    [InlineData("shared_rooms/123/room.png")]
    [InlineData("sharing/workshop/staging/item.json")]
    [InlineData("steam_appid.txt")]
    public void Machine_local_or_derived_files_are_not_cloud_eligible(string relativePath) =>
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(relativePath));

    [Fact]
    public void Character_backups_temporaries_quarantine_and_unknown_paint_are_not_cloud_eligible()
    {
        string root = $"characters/{CharacterId}";
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath($"{root}/character.json.bak"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath($"{root}/character.json.tmp"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath($"{root}/character.json.invalid-20260906"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath($"{root}/paint/unknown.png"));
    }

    [Theory]
    [InlineData("../progress.json")]
    [InlineData("characters/../progress.json")]
    [InlineData("/progress.json")]
    public void Traversal_and_rooted_paths_are_rejected(string relativePath) =>
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(relativePath));

    [Fact]
    public void Steamworks_rows_match_the_current_Windows_user_data_layout()
    {
        Assert.Equal("DesktopBuddy", SteamCloudSavePolicy.UserDataDirectoryName);
        Assert.Equal(5114950u, SteamCloudSavePolicy.FullGameAppId);
        Assert.Equal(5228990u, SteamCloudSavePolicy.DemoAppId);

        var rows = SteamCloudSavePolicy.WindowsAutoCloudRoots.ToArray();
        Assert.Collection(
            rows,
            row =>
            {
                Assert.Equal("DesktopBuddy", row.Subdirectory);
                Assert.Equal("progress.json", row.Pattern);
                Assert.False(row.Recursive);
            },
            row =>
            {
                Assert.Equal("DesktopBuddy/characters", row.Subdirectory);
                Assert.Equal("character.json", row.Pattern);
                Assert.True(row.Recursive);
            },
            row =>
            {
                Assert.Equal("DesktopBuddy/characters", row.Subdirectory);
                Assert.Equal("*.png", row.Pattern);
                Assert.True(row.Recursive);
            },
            row =>
            {
                Assert.Equal("DesktopBuddy/environment", row.Subdirectory);
                Assert.Equal("background.png", row.Pattern);
                Assert.False(row.Recursive);
            });
    }
}

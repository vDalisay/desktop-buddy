using System;
using System.IO;
using System.Linq;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Persistence;
using DesktopBuddy.Persistence.Characters;
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

    [Fact]
    public void Cloud_policy_matches_the_live_character_and_environment_paths()
    {
        Assert.Equal(SteamCloudSavePolicy.CharacterDocumentFileName, CharacterPaths.PrimaryFileName);
        Assert.Equal(
            $"{SteamCloudSavePolicy.EnvironmentDirectoryName}/{SteamCloudSavePolicy.EnvironmentBackgroundFileName}",
            EnvironmentCanvasPolicy.RelativePath);
    }

    [Fact]
    public void Project_configuration_keeps_Demo_and_full_game_on_the_same_local_user_root()
    {
        string? root = FindRepositoryRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);
        string project = File.ReadAllText(Path.Combine(root!, "project.godot"));
        string bootstrap = File.ReadAllText(Path.Combine(root, "src", "App", "Bootstrap.cs"));

        Assert.Contains("config/use_custom_user_dir=true", project, StringComparison.Ordinal);
        Assert.Contains(
            $"config/custom_user_dir_name=\"{SteamCloudSavePolicy.UserDataDirectoryName}\"",
            project,
            StringComparison.Ordinal);
        Assert.DoesNotContain("config/custom_user_dir_name.demo", project, StringComparison.Ordinal);
        Assert.DoesNotContain("config/custom_user_dir_name.steam_demo", project, StringComparison.Ordinal);

        Assert.Contains($"user://{SteamCloudSavePolicy.ProgressFileName}", bootstrap, StringComparison.Ordinal);
        Assert.Contains($"user://{SteamCloudSavePolicy.SettingsFileName}", bootstrap, StringComparison.Ordinal);
        Assert.Contains($"user://{SteamCloudSavePolicy.CharactersDirectoryName}", bootstrap, StringComparison.Ordinal);
    }

    private static string? FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "project.godot")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }
}

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
    private static readonly string BuddyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee").ToString("N");
    private static readonly string SceneId = Guid.Parse("12345678-1234-5678-9abc-def012345678").ToString("N");

    [Theory]
    [InlineData("progress.json")]
    [InlineData("progress.json.next")]
    [InlineData("work-progress.json")]
    [InlineData("work-progress.json.next")]
    [InlineData("scene-progress.commit.json")]
    [InlineData("environment/background.png")]
    public void Canonical_root_and_transaction_recovery_files_are_cloud_eligible(string relativePath) =>
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(relativePath));

    [Fact]
    public void Split_buddy_and_scene_documents_and_transaction_recovery_are_cloud_eligible()
    {
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"buddy-identities/{BuddyId}.json"));
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"buddy-identities/{BuddyId}.json.next"));
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath("scenes/index.json"));
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath("scenes/index.json.next"));
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"scenes/{SceneId}/scene.json"));
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"scenes/{SceneId}/scene.json.next"));
        Assert.True(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"scenes/{SceneId}/environment/background.png"));
    }

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
    [InlineData("progress.json.invalid-20260908")]
    [InlineData("work-progress.json.bak")]
    [InlineData("scene-progress.commit.json.bak")]
    [InlineData("scene-progress.commit.json.next")]
    [InlineData("shared_rooms/123/room.png")]
    [InlineData("sharing/workshop/staging/item.json")]
    [InlineData("steam_appid.txt")]
    public void Machine_local_or_uncommitted_derived_files_are_not_cloud_eligible(string relativePath) =>
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(relativePath));

    [Fact]
    public void Malformed_split_paths_and_nontransaction_recovery_are_not_cloud_eligible()
    {
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"buddy-identities/{BuddyId}.json.bak"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"buddy-identities/not-a-guid.json"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"buddy-identities/not-a-guid.json.next"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"scenes/{SceneId}/scene.json.bak"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"scenes/{SceneId}/environment/background.png.bak"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            "scenes/not-a-guid/scene.json"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            "scenes/not-a-guid/scene.json.next"));
        Assert.False(SteamCloudSavePolicy.IsCloudEligibleRelativePath(
            $"scenes/{SceneId}/sandbox.json"));
    }

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
            row => AssertRow(row, "DesktopBuddy", "progress.json", recursive: false),
            row => AssertRow(row, "DesktopBuddy", "progress.json.next", recursive: false),
            row => AssertRow(row, "DesktopBuddy", "work-progress.json", recursive: false),
            row => AssertRow(row, "DesktopBuddy", "work-progress.json.next", recursive: false),
            row => AssertRow(row, "DesktopBuddy", "scene-progress.commit.json", recursive: false),
            row => AssertRow(row, "DesktopBuddy/buddy-identities", "*.json", recursive: false),
            row => AssertRow(row, "DesktopBuddy/buddy-identities", "*.json.next", recursive: false),
            row => AssertRow(row, "DesktopBuddy/scenes", "index.json", recursive: false),
            row => AssertRow(row, "DesktopBuddy/scenes", "index.json.next", recursive: false),
            row => AssertRow(row, "DesktopBuddy/scenes", "scene.json", recursive: true),
            row => AssertRow(row, "DesktopBuddy/scenes", "scene.json.next", recursive: true),
            row => AssertRow(row, "DesktopBuddy/scenes", "background.png", recursive: true),
            row => AssertRow(row, "DesktopBuddy/characters", "character.json", recursive: true),
            row => AssertRow(row, "DesktopBuddy/characters", "*.png", recursive: true),
            row => AssertRow(row, "DesktopBuddy/environment", "background.png", recursive: false));
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

    private static void AssertRow(
        SteamAutoCloudRoot row,
        string subdirectory,
        string pattern,
        bool recursive)
    {
        Assert.Equal(subdirectory, row.Subdirectory);
        Assert.Equal(pattern, row.Pattern);
        Assert.Equal(recursive, row.Recursive);
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

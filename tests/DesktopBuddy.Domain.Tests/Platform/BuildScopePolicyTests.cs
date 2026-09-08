using System;
using System.IO;
using DesktopBuddy.Domain.Platform;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Platform;

public sealed class BuildScopePolicyTests
{
    [Theory]
    [InlineData(false, true, false, false, BuildSurface.SteamDemo, true, false, false, false)]
    [InlineData(false, true, true, false, BuildSurface.NextFestDemo, true, true, false, false)]
    [InlineData(false, false, false, true, BuildSurface.FullRelease, false, false, true, false)]
    [InlineData(true, false, false, false, BuildSurface.ItchIo, false, false, false, true)]
    [InlineData(false, false, false, false, BuildSurface.UntaggedFallback, false, false, false, false)]
    public void Canonical_build_matrix_resolves_exactly(
        bool itchIo,
        bool steamDemo,
        bool nextFestDemo,
        bool fullRelease,
        BuildSurface expectedSurface,
        bool expectedSteamDemo,
        bool expectedNextFestDemo,
        bool expectedFullRelease,
        bool expectedItchIo)
    {
        BuildScopePolicy policy = BuildScopePolicy.Resolve(
            itchIo,
            steamDemo,
            nextFestDemo,
            fullRelease);

        Assert.Equal(expectedSurface, policy.Surface);
        Assert.Equal(expectedSteamDemo, policy.IsSteamDemo);
        Assert.Equal(expectedNextFestDemo, policy.IsNextFestDemo);
        Assert.Equal(expectedFullRelease, policy.IsFullRelease);
        Assert.Equal(expectedItchIo, policy.IsItchIo);
        Assert.Equal(expectedSurface == BuildSurface.SteamDemo, policy.IsInitialSteamDemo);
        Assert.Equal(expectedSurface == BuildSurface.UntaggedFallback, policy.IsUntaggedFallback);
    }

    [Fact]
    public void Next_Fest_is_still_the_Steam_Demo_product()
    {
        BuildScopePolicy policy = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo: true,
            nextFestDemo: true,
            fullRelease: false);

        Assert.True(policy.IsSteamDemo);
        Assert.True(policy.IsNextFestDemo);
        Assert.False(policy.IsInitialSteamDemo);
        Assert.False(policy.IsFullRelease);
    }

    [Fact]
    public void Itch_scope_wins_over_every_wider_tag()
    {
        BuildScopePolicy policy = BuildScopePolicy.Resolve(
            itchIo: true,
            steamDemo: true,
            nextFestDemo: true,
            fullRelease: true);

        Assert.Equal(BuildSurface.ItchIo, policy.Surface);
        Assert.True(policy.IsItchIo);
        Assert.False(policy.IsSteamDemo);
        Assert.False(policy.IsNextFestDemo);
        Assert.False(policy.IsFullRelease);
    }

    [Theory]
    [InlineData(true, false, true, BuildSurface.SteamDemo)]
    [InlineData(true, true, true, BuildSurface.SteamDemo)]
    [InlineData(false, true, true, BuildSurface.UntaggedFallback)]
    public void Full_release_mixed_with_demo_family_tags_fails_closed(
        bool steamDemo,
        bool nextFestDemo,
        bool fullRelease,
        BuildSurface expected)
    {
        BuildScopePolicy policy = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo,
            nextFestDemo,
            fullRelease);

        Assert.Equal(expected, policy.Surface);
        Assert.False(policy.IsFullRelease);
        Assert.False(policy.IsNextFestDemo);
    }

    [Fact]
    public void Next_Fest_tag_without_Steam_Demo_tag_fails_to_untagged_scope()
    {
        BuildScopePolicy policy = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo: false,
            nextFestDemo: true,
            fullRelease: false);

        Assert.Equal(BuildSurface.UntaggedFallback, policy.Surface);
        Assert.False(policy.IsSteamDemo);
        Assert.False(policy.IsNextFestDemo);
    }

    [Theory]
    [InlineData(false, true, false, false, false)] // Initial Steam Demo
    [InlineData(false, true, true, false, true)]   // Next Fest Demo
    [InlineData(false, false, false, true, true)]  // Full Release
    [InlineData(true, false, false, false, false)] // itch.io
    [InlineData(false, false, false, false, false)] // untagged fallback
    [InlineData(false, true, true, true, false)]   // malformed Full + Demo family
    [InlineData(false, false, true, false, false)] // stray next_fest_demo
    public void Room_Decorator_is_exposed_only_on_scene_owned_release_surfaces(
        bool itchIo,
        bool steamDemo,
        bool nextFestDemo,
        bool fullRelease,
        bool expected)
    {
        BuildScopePolicy policy = BuildScopePolicy.Resolve(
            itchIo,
            steamDemo,
            nextFestDemo,
            fullRelease);

        Assert.Equal(expected, policy.IncludesRoomDecorator);
        Assert.Equal(policy.IncludesScenes, policy.IncludesRoomDecorator);
    }

    [Fact]
    public void Export_presets_encode_the_master_plan_feature_tags_exactly()
    {
        string? root = FindRepositoryRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);
        string config = File.ReadAllText(Path.Combine(root!, "export_presets.cfg"));

        Assert.Equal(
            "steam,steam_demo",
            CustomFeaturesForPreset(config, "Windows Steam Demo"));
        Assert.Equal(
            "steam,steam_demo,next_fest_demo",
            CustomFeaturesForPreset(config, "Windows Steam Next Fest Demo"));
        Assert.Equal(
            "steam,full_release",
            CustomFeaturesForPreset(config, "Windows Full Release"));
        Assert.Equal(
            "itch_io,protected_build,web_experimental",
            CustomFeaturesForPreset(config, "Web itch.io experimental"));
    }

    private static string CustomFeaturesForPreset(string config, string presetName)
    {
        string nameToken = $"name=\"{presetName}\"";
        int nameIndex = config.IndexOf(nameToken, StringComparison.Ordinal);
        Assert.True(nameIndex >= 0, $"Missing export preset {presetName}.");

        int nextPreset = config.IndexOf("\n[preset.", nameIndex + nameToken.Length, StringComparison.Ordinal);
        int featureIndex = config.IndexOf("custom_features=\"", nameIndex, StringComparison.Ordinal);
        Assert.True(
            featureIndex >= 0 && (nextPreset < 0 || featureIndex < nextPreset),
            $"Preset {presetName} has no custom_features entry.");

        const string prefix = "custom_features=\"";
        int valueStart = featureIndex + prefix.Length;
        int valueEnd = config.IndexOf('"', valueStart);
        Assert.True(valueEnd > valueStart, $"Preset {presetName} has an invalid custom_features entry.");
        return config[valueStart..valueEnd];
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

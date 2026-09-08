using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class RuntimeProgressBindingTests
{
    [Fact]
    public void Split_player_binding_preserves_null_extensions_then_exposes_new_value()
    {
        PlayerProgressState player = Player();
        var binding = new PlayerRuntimeProgressBinding(player);

        Assert.Null(binding.Extensions);
        Assert.True(binding.SetExtensionValue("test.flag", "enabled"));
        Assert.Equal("enabled", binding.Extensions!.Values!["test.flag"]);
    }

    [Fact]
    public void Split_buddy_binding_preserves_null_player_extensions_then_exposes_new_value()
    {
        PlayerProgressState player = Player();
        BuddyIdentityState buddy = Buddy();
        var binding = new BuddyRuntimeProgressBinding(new BuddyProgressCoordinator(player, buddy));

        Assert.Null(binding.Extensions);
        Assert.True(binding.SetExtensionValue("test.flag", "enabled"));
        Assert.Equal("enabled", binding.Extensions!.Values!["test.flag"]);
    }

    [Fact]
    public void Legacy_bindings_retain_nullable_extension_behavior()
    {
        var legacy = new BuddyProgressState(0.01);
        var player = new PlayerRuntimeProgressBinding(legacy);
        var buddy = new BuddyRuntimeProgressBinding(legacy);

        Assert.Null(player.Extensions);
        Assert.Null(buddy.Extensions);
        Assert.True(player.SetExtensionValue("legacy.flag", "yes"));
        Assert.Equal("yes", buddy.Extensions!.Values!["legacy.flag"]);
    }

    private static PlayerProgressState Player() => new(
        0.01,
        new PlayerProgressSnapshot(
            Revision: 0,
            BalanceMilliCredits: 0,
            SelectedToolId: ContentIds.ToolGrab,
            UnlockedContentIds: [ContentIds.ToolGrab],
            Statistics: new ProgressStatistics(0, 0, 0, 0, 0),
            Times: new CumulativeTimes(0.0, 0.0, 0.0),
            Extensions: null));

    private static BuddyIdentityState Buddy() => new(
        new BuddyIdentitySnapshot(
            BuddyIdentityId.From(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee")),
            Revision: 0,
            CharacterId: null,
            Mood: 0.0f,
            Fullness: 50.0f,
            HarmfulContentIds: Array.Empty<string>(),
            Traits: BuddyTraits.Default,
            FunInterest: null));
}

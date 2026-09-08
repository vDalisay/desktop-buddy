using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class PlayerRuntimeProgressBindingTests
{
    [Fact]
    public void Legacy_and_split_bindings_route_account_semantics_to_their_real_owner()
    {
        var legacy = new BuddyProgressState(
            cashPerPain: 0.01,
            unlockedToolIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
            initialBalanceMilliCredits: 5_000);
        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(legacy.Snapshot(), activeCharacterId: null);
        var player = new PlayerProgressState(0.01, partition.Player);
        var legacyBinding = new PlayerRuntimeProgressBinding(legacy);
        var splitBinding = new PlayerRuntimeProgressBinding(player);

        Assert.False(legacyBinding.IsSplit);
        Assert.True(splitBinding.IsSplit);
        Assert.Equal(5_000, legacyBinding.BalanceMilliCredits);
        Assert.Equal(5_000, splitBinding.BalanceMilliCredits);
        Assert.True(legacyBinding.IsUnlocked(ContentIds.ToolPistol));
        Assert.True(splitBinding.IsUnlocked(ContentIds.ToolPistol));

        Assert.True(legacyBinding.SelectTool(ToolId.Pistol));
        Assert.True(splitBinding.SelectTool(ToolId.Pistol));
        Assert.Equal(ToolId.Pistol, legacy.SelectedTool);
        Assert.Equal(ToolId.Pistol, player.SelectedTool);

        Assert.True(legacyBinding.SetExtensionValue("test.account", "legacy"));
        Assert.True(splitBinding.SetExtensionValue("test.account", "split"));
        Assert.Equal("legacy", legacy.Extensions!.Values!["test.account"]);
        Assert.Equal("split", player.Extensions!.Values!["test.account"]);
    }

    [Fact]
    public void Split_binding_has_no_buddy_local_surface()
    {
        var legacy = new BuddyProgressState(cashPerPain: 0.01, initialMood: -55.0f, initialFullness: 12.0f);
        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(legacy.Snapshot(), activeCharacterId: null);
        var player = new PlayerProgressState(0.01, partition.Player);
        var binding = new PlayerRuntimeProgressBinding(player);

        // Compile-time API shape is the invariant: only account fields are exposed. Runtime proof
        // confirms the binding holds no legacy aggregate that could smuggle Buddy state into UI.
        Assert.Null(binding.LegacyProgress);
        Assert.Same(player, binding.PlayerProgress);
    }
}

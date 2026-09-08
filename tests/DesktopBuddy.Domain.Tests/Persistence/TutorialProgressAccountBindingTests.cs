using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class TutorialProgressAccountBindingTests
{
    [Fact]
    public void Split_tutorial_writes_account_extensions_without_touching_buddy_identity()
    {
        var legacy = new BuddyProgressState(cashPerPain: 0.01, initialMood: -25.0f);
        LegacyProgressPartition partition = LegacyProgressPartitionPolicy.Split(
            legacy.Snapshot(),
            activeCharacterId: null);
        var player = new PlayerProgressState(0.01, partition.Player);
        var buddy = new BuddyIdentityState(partition.Buddy);
        BuddyIdentitySnapshot buddyBefore = buddy.Snapshot();
        var tutorial = new TutorialProgressState(new PlayerRuntimeProgressBinding(player));

        Assert.True(tutorial.MarkCompleted(TutorialStepIds.GrabBuddy));
        Assert.True(tutorial.HasPersistedRecord);
        Assert.True(tutorial.IsCompleted(TutorialStepIds.GrabBuddy));
        Assert.Equal(
            TutorialStepIds.GrabBuddy,
            player.Extensions!.Values![TutorialProgressState.ExtensionKey]);

        BuddyIdentitySnapshot buddyAfter = buddy.Snapshot();
        Assert.Equal(buddyBefore.BuddyIdentityId, buddyAfter.BuddyIdentityId);
        Assert.Equal(buddyBefore.Revision, buddyAfter.Revision);
        Assert.Equal(buddyBefore.CharacterId, buddyAfter.CharacterId);
        Assert.Equal(buddyBefore.Mood, buddyAfter.Mood);
        Assert.Equal(buddyBefore.Fullness, buddyAfter.Fullness);
        Assert.Equal(buddyBefore.HarmfulContentIds, buddyAfter.HarmfulContentIds);
        Assert.Equal(buddyBefore.Traits, buddyAfter.Traits);
        Assert.Equal(buddyBefore.FunInterest, buddyAfter.FunInterest);
    }

    [Fact]
    public void Legacy_constructor_preserves_initial_demo_behavior()
    {
        var legacy = new BuddyProgressState(cashPerPain: 0.01);
        var tutorial = new TutorialProgressState(legacy);

        Assert.True(tutorial.Skip());
        Assert.True(tutorial.IsComplete);
        Assert.Equal(
            "skip",
            legacy.Extensions!.Values![TutorialProgressState.ExtensionKey]);
    }
}

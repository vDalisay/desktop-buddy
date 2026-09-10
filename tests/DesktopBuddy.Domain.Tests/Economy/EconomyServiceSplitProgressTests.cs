using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Economy;

public sealed class EconomyServiceSplitProgressTests
{
    [Fact]
    public void Split_service_returns_no_feedback_without_falling_back_to_legacy_state()
    {
        var service = new EconomyService(CreatePlayer(initialBalance: 0), new ToolCatalogue([]));

        Assert.Null(service.PollFeedback(1.0));
    }

    [Fact]
    public void Split_damage_uses_one_wallet_and_only_the_target_buddy_state()
    {
        PlayerProgressState player = CreatePlayer(initialBalance: 25_000);
        BuddyIdentityState target = CreateBuddy("11111111-1111-1111-1111-111111111111");
        BuddyIdentityState untouched = CreateBuddy("22222222-2222-2222-2222-222222222222");
        var targetProgress = new BuddyProgressCoordinator(player, target);
        var economy = new EconomyService(player, new ToolCatalogue([]));
        long announcedBalance = -1;
        economy.BalanceChanged += balance => announcedBalance = balance;

        long before = player.BalanceMilliCredits;
        long awarded = economy.AcceptDamage(
            targetProgress,
            ContentIds.ToolPistol,
            pain: 10.0f,
            PayoutRegion.Head,
            DamageConsciousness.Conscious,
            now: 1.0,
            ImpactMoodEffect.Harm);

        Assert.True(awarded > 0);
        Assert.Equal(before + awarded, player.BalanceMilliCredits);
        Assert.Equal(player.BalanceMilliCredits, announcedBalance);
        Assert.True(target.Mood < 0.0f);
        Assert.True(target.IsContentHarmful(ContentIds.ToolPistol));
        Assert.Equal(0.0f, untouched.Mood);
        Assert.False(untouched.IsContentHarmful(ContentIds.ToolPistol));
        Assert.Equal(1, player.Statistics.ScoredImpacts);
    }

    [Fact]
    public void Split_damage_rejects_a_coordinator_from_another_player_account()
    {
        PlayerProgressState owner = CreatePlayer(initialBalance: 10_000);
        PlayerProgressState foreign = CreatePlayer(initialBalance: 20_000);
        BuddyIdentityState foreignBuddy = CreateBuddy("33333333-3333-3333-3333-333333333333");
        var economy = new EconomyService(owner, new ToolCatalogue([]));
        var wrongTarget = new BuddyProgressCoordinator(foreign, foreignBuddy);

        long ownerBefore = owner.BalanceMilliCredits;
        long foreignBefore = foreign.BalanceMilliCredits;
        float moodBefore = foreignBuddy.Mood;

        Assert.Throws<InvalidOperationException>(() => economy.AcceptDamage(
            wrongTarget,
            ContentIds.ToolPistol,
            pain: 10.0f,
            PayoutRegion.Head,
            DamageConsciousness.Conscious,
            now: 1.0,
            ImpactMoodEffect.Harm));

        Assert.Equal(ownerBefore, owner.BalanceMilliCredits);
        Assert.Equal(foreignBefore, foreign.BalanceMilliCredits);
        Assert.Equal(moodBefore, foreignBuddy.Mood);
    }

    [Fact]
    public void Legacy_economy_constructor_keeps_aggregate_damage_semantics()
    {
        var progress = new BuddyProgressState(cashPerPain: 1.0, initialBalanceMilliCredits: 5_000);
        var economy = new EconomyService(progress, new ToolCatalogue([]));
        long before = progress.BalanceMilliCredits;

        long awarded = economy.AcceptDamage(
            ContentIds.ToolPistol,
            pain: 10.0f,
            PayoutRegion.Head,
            DamageConsciousness.Conscious,
            now: 1.0,
            ImpactMoodEffect.Harm);

        Assert.True(awarded > 0);
        Assert.Equal(before + awarded, progress.BalanceMilliCredits);
        Assert.True(progress.Mood < 0.0f);
        Assert.True(progress.IsContentHarmful(ContentIds.ToolPistol));
    }

    [Fact]
    public void Player_backed_passive_income_and_unlocks_remain_account_global()
    {
        PlayerProgressState player = CreatePlayer(initialBalance: 1_000);
        var economy = new EconomyService(player, new ToolCatalogue([]));
        long announcedBalance = -1;
        economy.BalanceChanged += balance => announcedBalance = balance;

        economy.DepositPassive(4_000);
        bool unlocked = economy.Unlock(ContentIds.ToolPistol);

        Assert.Equal(5_000, economy.BalanceMilliCredits);
        Assert.Equal(5_000, announcedBalance);
        Assert.True(unlocked);
        Assert.True(economy.IsUnlocked(ContentIds.ToolPistol));
        Assert.True(player.IsUnlocked(ContentIds.ToolPistol));
    }

    private static PlayerProgressState CreatePlayer(long initialBalance)
    {
        var snapshot = new PlayerProgressSnapshot(
            Revision: 0,
            BalanceMilliCredits: initialBalance,
            SelectedToolId: ContentIds.ToolGrab,
            UnlockedContentIds: [ContentIds.ToolGrab],
            Statistics: new ProgressStatistics(0, 0, 0, 0, 0),
            Times: new CumulativeTimes(0, 0, 0));
        return new PlayerProgressState(cashPerPain: 1.0, snapshot);
    }

    private static BuddyIdentityState CreateBuddy(string id)
    {
        var snapshot = new BuddyIdentitySnapshot(
            BuddyIdentityId.From(Guid.Parse(id)),
            Revision: 0,
            CharacterId: null,
            Mood: 0.0f,
            Fullness: 100.0f,
            HarmfulContentIds: Array.Empty<string>(),
            Traits: BuddyTraits.Default,
            FunInterest: null);
        return new BuddyIdentityState(snapshot);
    }
}

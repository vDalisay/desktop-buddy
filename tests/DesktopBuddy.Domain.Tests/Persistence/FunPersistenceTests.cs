using System.Linq;
using System.Text.Json;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class FunPersistenceTests
{
    private const double CashPerPain = 1.0;

    private static FunPreferences DistinctTastes => new(
        CatchDrain: 1, PetDrain: 7, TickleDrain: 20, TreatDrain: 12);

    /// <summary>Taste is identity: it must come back off disk exactly as it was rolled.</summary>
    [Fact]
    public void TastesSurviveASaveRoundTrip()
    {
        var state = new BuddyProgressState(
            CashPerPain, traits: new BuddyTraits(50, DistinctTastes));

        ProgressSave save = ProgressSave.FromSnapshot(state.Snapshot());
        SaveDecodeResult decoded = ProgressSavePolicy.Decode(ProgressSavePolicy.Serialize(save));
        BuddyProgressState restored = ProgressSavePolicy.CreateState(decoded.Save!, CashPerPain);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(DistinctTastes, restored.Traits.Preferences);
    }

    /// <summary>A buddy that got bored yesterday is still bored when the game reopens.</summary>
    [Fact]
    public void SpentInterestSurvivesASaveRoundTrip()
    {
        var state = new BuddyProgressState(
            CashPerPain, traits: new BuddyTraits(50, DistinctTastes));
        state.EngageFun(FunActivityId.Tickle);
        state.EngageFun(FunActivityId.Tickle);
        float expected = state.InterestIn(FunActivityId.Tickle);

        ProgressSave save = ProgressSave.FromSnapshot(state.Snapshot());
        SaveDecodeResult decoded = ProgressSavePolicy.Decode(ProgressSavePolicy.Serialize(save));
        BuddyProgressState restored = ProgressSavePolicy.CreateState(decoded.Save!, CashPerPain);

        Assert.Equal(60.0f, expected);
        Assert.Equal(expected, restored.InterestIn(FunActivityId.Tickle));
        Assert.Equal(100.0f, restored.InterestIn(FunActivityId.Catch));
    }

    [Fact]
    public void EveryKnownActivityIsWritten()
    {
        var state = new BuddyProgressState(CashPerPain);

        ProgressSave save = ProgressSave.FromSnapshot(state.Snapshot());

        Assert.Equal(FunInterestModel.ActivityCount, save.FunActivities.Count);
        Assert.Contains(save.FunActivities, entry => entry.ActivityId == ContentIds.FunCatch);
        Assert.Contains(save.FunActivities, entry => entry.ActivityId == ContentIds.FunPet);
        Assert.Contains(save.FunActivities, entry => entry.ActivityId == ContentIds.FunTickle);
        Assert.Contains(save.FunActivities, entry => entry.ActivityId == ContentIds.FunTreat);
    }

    /// <summary>
    /// A save written before the feature existed belongs to a buddy whose tastes were never
    /// rolled. It must migrate to the neutral default deterministically — not to a fresh
    /// random personality, which would differ on every load.
    /// </summary>
    [Fact]
    public void SavesPredatingTheFeature_MigrateToNeutralTastesDeterministically()
    {
        string legacy = LegacyV2Payload();

        SaveDecodeResult first = ProgressSavePolicy.Decode(legacy);
        SaveDecodeResult second = ProgressSavePolicy.Decode(legacy);

        Assert.Equal(SaveDecodeStatus.Valid, first.Status);
        BuddyProgressState firstState = ProgressSavePolicy.CreateState(first.Save!, CashPerPain);
        BuddyProgressState secondState = ProgressSavePolicy.CreateState(second.Save!, CashPerPain);
        Assert.Equal(FunPreferences.Default, firstState.Traits.Preferences);
        Assert.Equal(firstState.Traits.Preferences, secondState.Traits.Preferences);
        foreach (FunActivityId activity in System.Enum.GetValues<FunActivityId>())
        {
            Assert.Equal(FunInterestModel.MaximumInterest, firstState.InterestIn(activity));
        }
    }

    [Fact]
    public void MigratedLegacySave_KeepsItsOtherState()
    {
        SaveDecodeResult decoded = ProgressSavePolicy.Decode(LegacyV2Payload());

        BuddyProgressState state = ProgressSavePolicy.CreateState(decoded.Save!, CashPerPain);

        Assert.Equal(ProgressSave.CurrentSchemaVersion, decoded.Save!.SchemaVersion);
        Assert.Equal(42.0f, state.Mood);
        Assert.Equal(77, state.Traits.ObstacleHopPropensity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-3)]
    public void OutOfRangeDrain_IsRejectedAsCorruption(int drain)
    {
        var state = new BuddyProgressState(CashPerPain);
        ProgressSave save = ProgressSave.FromSnapshot(state.Snapshot());
        ProgressSave corrupt = save with
        {
            FunActivities = save.FunActivities
                .Select(entry => entry.ActivityId == ContentIds.FunCatch
                    ? entry with { Drain = drain }
                    : entry)
                .ToList(),
        };

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(
            JsonSerializer.Serialize(corrupt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(SaveDecodeStatus.Invalid, decoded.Status);
    }

    /// <summary>
    /// Only finite out-of-range values are exercised here: JSON has no NaN literal, so a
    /// non-finite meter cannot reach this path from a file at all. The model's own clamp
    /// covers NaN, and <c>FunInterestModelTests</c> pins it.
    /// </summary>
    [Theory]
    [InlineData(-1.0f)]
    [InlineData(101.0f)]
    public void OutOfRangeInterest_IsRejectedAsCorruption(float interest)
    {
        var state = new BuddyProgressState(CashPerPain);
        ProgressSave save = ProgressSave.FromSnapshot(state.Snapshot());
        ProgressSave corrupt = save with
        {
            FunActivities = save.FunActivities
                .Select(entry => entry.ActivityId == ContentIds.FunCatch
                    ? entry with { Interest = interest }
                    : entry)
                .ToList(),
        };

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(
            JsonSerializer.Serialize(corrupt, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Equal(SaveDecodeStatus.Invalid, decoded.Status);
    }

    /// <summary>An activity a newer build added must survive without activating here.</summary>
    [Fact]
    public void UnknownActivityEntry_IsIgnoredWithoutFailingTheLoad()
    {
        var state = new BuddyProgressState(
            CashPerPain, traits: new BuddyTraits(50, DistinctTastes));
        ProgressSave save = ProgressSave.FromSnapshot(state.Snapshot());
        ProgressSave forward = save with
        {
            FunActivities = [.. save.FunActivities, new FunActivitySave
            {
                ActivityId = "fun.from_a_later_build",
                Drain = 9,
                Interest = 33.0f,
            }],
        };

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(
            JsonSerializer.Serialize(forward, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        BuddyProgressState restored = ProgressSavePolicy.CreateState(decoded.Save!, CashPerPain);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(DistinctTastes, restored.Traits.Preferences);
    }

    /// <summary>Recharge is persistent state, so it must bump the save revision.</summary>
    [Fact]
    public void EngagingAndRechargingMarkTheSaveDirty()
    {
        var state = new BuddyProgressState(CashPerPain);
        long start = state.Revision;

        state.EngageFun(FunActivityId.Catch);
        long afterEngage = state.Revision;
        state.RechargeFun(1.0);

        Assert.True(afterEngage > start);
        Assert.True(state.Revision > afterEngage);
    }

    private static string LegacyV2Payload() =>
        """
        {
          "schemaVersion": 2,
          "revision": 12,
          "balanceMilliCredits": 500,
          "unlockedToolIds": ["tool.grab", "tool.pet"],
          "selectedToolId": "tool.pet",
          "mood": 42.0,
          "harmfulContentIds": [],
          "obstacleHopPropensity": 77,
          "statistics": {},
          "times": {},
          "extensions": {}
        }
        """;
}

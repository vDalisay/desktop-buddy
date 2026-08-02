using System.Text.Json;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class ProgressSavePolicyTests
{
    private const double CashPerPain = 0.5;

    [Fact]
    public void CurrentSave_RoundTripsEverySemanticField()
    {
        var source = new ProgressSave
        {
            Revision = 42,
            BalanceMilliCredits = 12_345,
            UnlockedToolIds = [ContentIds.ToolGrab, ContentIds.ToolPet],
            SelectedToolId = ContentIds.ToolPet,
            Mood = -35.5f,
            HarmfulContentIds = [ContentIds.ToolBoxingGlove, ContentIds.LooseObject],
            ObstacleHopPropensity = 73,
            Statistics = new ProgressStatisticsSave
            {
                ScoredImpacts = 9,
                Knockouts = 2,
                CareAwards = 4,
                TrustResets = 1,
                EarnedMilliCredits = 20_000,
                SuccessfulCatches = 3,
                ToolUses = { [ContentIds.ToolPet] = 7 },
            },
            Times = new CumulativeTimesSave
            {
                RunSeconds = 120,
                ActiveSeconds = 90,
                HiddenSeconds = 30,
            },
            Extensions = new ProgressExtensionsSave
            {
                UnknownContentIds = ["tool.future"],
                Values = { ["futureFlag"] = "kept" },
            },
        };

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(source));

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.NotNull(decoded.Save);
        Assert.Equal(42, decoded.Save.Revision);
        Assert.Equal(12_345, decoded.Save.BalanceMilliCredits);
        Assert.Equal(ContentIds.ToolPet, decoded.Save.SelectedToolId);
        Assert.Equal(73, decoded.Save.ObstacleHopPropensity);
        Assert.Equal(3, decoded.Save.Statistics.SuccessfulCatches);
        Assert.Equal(7, decoded.Save.Statistics.ToolUses[ContentIds.ToolPet]);
        Assert.Equal("kept", decoded.Save.Extensions.Values["futureFlag"]);
    }

    [Fact]
    public void Fullness_SurvivesTheRoundTripAndReachesTheRestoredState()
    {
        // Appetite is semantic state like mood: a relaunch must not reset the buddy's stomach.
        var progress = new BuddyProgressState(1.0);
        progress.FillHunger(140.0f);

        ProgressSave save = ProgressSave.FromSnapshot(progress.Snapshot());
        ProgressSave decoded = ProgressSavePolicy.Decode(
            ProgressSavePolicy.Serialize(save)).Save!;
        BuddyProgressState restored = ProgressSavePolicy.CreateState(decoded, 1.0);

        Assert.Equal(140.0f, decoded.Fullness);
        Assert.Equal(140.0f, restored.Fullness);
        Assert.False(restored.WouldEat(80.0f));
        Assert.True(restored.WouldEat(60.0f));
    }

    [Fact]
    public void APreHungerSaveResumesWithAnEmptyStomach()
    {
        // Schema 4 has no stomach state. Resuming full would silently refuse the first meal
        // the player offered after upgrading.
        string legacy = Downgrade(
            ProgressSavePolicy.Serialize(new ProgressSave { Mood = 12.0f }),
            4);

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(legacy);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(ProgressSave.CurrentSchemaVersion, decoded.Save!.SchemaVersion);
        Assert.Equal(0.0f, decoded.Save.Fullness);
        Assert.Equal(12.0f, decoded.Save.Mood);
    }

    [Fact]
    public void V1IntegerIds_MigrateSequentiallyToStableStrings()
    {
        const string legacy = """
            {
              "schemaVersion": 1,
              "revision": 8,
              "balanceMilliCredits": 2500,
              "selectedTool": 3,
              "unlockedTools": [0, 1, 3],
              "mood": -12.5,
              "harmfulTools": [3],
              "obstacleHopPropensity": 81
            }
            """;

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(legacy);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(ProgressSave.CurrentSchemaVersion, decoded.Save!.SchemaVersion);
        Assert.Equal(ContentIds.ToolBoxingGlove, decoded.Save.SelectedToolId);
        Assert.Contains(ContentIds.ToolPet, decoded.Save.UnlockedToolIds);
        Assert.Equal([ContentIds.ToolBoxingGlove], decoded.Save.HarmfulContentIds);
        Assert.Equal(81, decoded.Save.ObstacleHopPropensity);
    }

    [Fact]
    public void UnknownLegacyIds_AreRetainedButNotActivated()
    {
        const string legacy = """
            {
              "schemaVersion": 1,
              "selectedTool": 99,
              "unlockedTools": [0, 99],
              "harmfulTools": [99]
            }
            """;

        ProgressSave save = ProgressSavePolicy.Decode(legacy).Save!;
        BuddyProgressState state = ProgressSavePolicy.CreateState(save, CashPerPain);

        Assert.Equal(ToolId.Grab, state.SelectedTool);
        Assert.False(state.IsToolUnlocked("legacy.tool.99"));
        Assert.Contains("legacy.tool.99", state.Extensions!.UnknownContentIds!);
        Assert.Equal("legacy.tool.99", state.Extensions.UnknownSelectedToolId);
    }

    [Fact]
    public void UnknownCurrentSelectedTool_FallsBackAndSurvivesNextSave()
    {
        var save = new ProgressSave
        {
            UnlockedToolIds = [ContentIds.ToolGrab, "tool.from_future"],
            SelectedToolId = "tool.from_future",
        };

        BuddyProgressState state = ProgressSavePolicy.CreateState(save, CashPerPain);
        ProgressSave next = ProgressSave.FromSnapshot(state.Snapshot());

        Assert.Equal(ToolId.Grab, state.SelectedTool);
        Assert.Equal("tool.from_future", next.Extensions.UnknownSelectedToolId);
        Assert.False(state.IsToolUnlocked("tool.from_future"));
        Assert.Contains("tool.from_future", next.Extensions.UnknownContentIds);
    }

    [Fact]
    public void LoadedState_SaveRoundTrip_PreservesFullStatistics()
    {
        var save = new ProgressSave
        {
            UnlockedToolIds = [ContentIds.ToolGrab],
            Statistics = new ProgressStatisticsSave
            {
                ScoredImpacts = 9,
                Knockouts = 2,
                CareAwards = 4,
                TrustResets = 1,
                EarnedMilliCredits = 20_000,
                SuccessfulCatches = 3,
                TotalPainMilli = 88_000,
                BestOneSecondMilliCredits = 4_000,
                BestThreeSecondMilliCredits = 9_000,
                BestTenSecondMilliCredits = 15_000,
                HighestMood = 72,
                LowestMood = -48,
                ToolUses = { [ContentIds.ToolPet] = 7 },
                ToolPainMilli = { [ContentIds.ToolBoxingGlove] = 55_000 },
            },
        };

        BuddyProgressState state = ProgressSavePolicy.CreateState(save, CashPerPain);
        ProgressStatisticsSave restored =
            ProgressSave.FromSnapshot(state.Snapshot()).Statistics;

        Assert.Equal(3, restored.SuccessfulCatches);
        Assert.Equal(88_000, restored.TotalPainMilli);
        Assert.Equal(15_000, restored.BestTenSecondMilliCredits);
        Assert.Equal(72, restored.HighestMood);
        Assert.Equal(-48, restored.LowestMood);
        Assert.Equal(7, restored.ToolUses[ContentIds.ToolPet]);
        Assert.Equal(55_000, restored.ToolPainMilli[ContentIds.ToolBoxingGlove]);
    }

    [Fact]
    public void FutureSchema_IsRejectedWithoutBeingClassifiedAsCorrupt()
    {
        SaveDecodeResult decoded = ProgressSavePolicy.Decode(
            """{"schemaVersion":999,"revision":1}""");

        Assert.Equal(SaveDecodeStatus.UnsupportedFutureVersion, decoded.Status);
        Assert.Null(decoded.Save);
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"mood":"nope"}""", SaveDecodeStatus.Malformed)]
    [InlineData("""{"schemaVersion":2,"mood":101}""", SaveDecodeStatus.Invalid)]
    [InlineData("""{"schemaVersion":2,"balanceMilliCredits":-1}""", SaveDecodeStatus.Invalid)]
    [InlineData("""{"schemaVersion":2,"statistics":null}""", SaveDecodeStatus.Invalid)]
    [InlineData("""{"schemaVersion":2,"unlockedToolIds":null}""", SaveDecodeStatus.Invalid)]
    public void MalformedAndInvalidPayloads_AreClassified(string json, SaveDecodeStatus expected)
    {
        Assert.Equal(expected, ProgressSavePolicy.Decode(json).Status);
    }

    [Fact]
    public void SaveSchema_ContainsNoLiveSimulationFields()
    {
        string json = ProgressSavePolicy.Serialize(new ProgressSave
        {
            UnlockedToolIds = [ContentIds.ToolGrab],
        });
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.False(root.TryGetProperty("transform", out _));
        Assert.False(root.TryGetProperty("velocity", out _));
        Assert.False(root.TryGetProperty("looseObjects", out _));
        Assert.False(root.TryGetProperty("painWindow", out _));
        Assert.False(root.TryGetProperty("knockout", out _));
        Assert.False(root.TryGetProperty("cooldowns", out _));
        Assert.False(root.TryGetProperty("activity", out _));
        Assert.False(root.TryGetProperty("grab", out _));
    }

    [Fact]
    public void StateFactory_RestoresRevisionBalanceSelectionAndTraits()
    {
        var save = new ProgressSave
        {
            Revision = 91,
            BalanceMilliCredits = 44_500,
            UnlockedToolIds = [ContentIds.ToolGrab, ContentIds.ToolTickle],
            SelectedToolId = ContentIds.ToolTickle,
            Mood = 45,
            ObstacleHopPropensity = 6,
        };

        BuddyProgressState state = ProgressSavePolicy.CreateState(save, CashPerPain);

        Assert.Equal(91, state.Revision);
        Assert.Equal(44_500, state.BalanceMilliCredits);
        Assert.Equal(ToolId.Tickle, state.SelectedTool);
        Assert.Equal(6, state.Traits.ObstacleHopPropensity);
        Assert.Equal(45, state.Mood);
    }

    /// <summary>
    /// A v1 payload is written by an older build, so a wrong-typed legacy field is
    /// corruption to be classified and quarantined — never an exception that escapes
    /// <see cref="ProgressSavePolicy.Decode"/> and takes the launch down with it.
    /// </summary>
    [Theory]
    [InlineData("""{"schemaVersion":1,"selectedTool":"grab"}""")]
    [InlineData("""{"schemaVersion":1,"revision":"eight"}""")]
    [InlineData("""{"schemaVersion":1,"balanceMilliCredits":true}""")]
    [InlineData("""{"schemaVersion":1,"mood":"sad"}""")]
    [InlineData("""{"schemaVersion":1,"obstacleHopPropensity":"high"}""")]
    [InlineData("""{"schemaVersion":1,"unlockedTools":["grab"]}""")]
    [InlineData("""{"schemaVersion":1,"harmfulTools":[{"id":3}]}""")]
    [InlineData("""{"schemaVersion":1,"selectedTool":99999999999999999999}""")]
    public void WrongTypedLegacyFields_AreMalformedNotThrown(string legacy)
    {
        SaveDecodeResult decoded = ProgressSavePolicy.Decode(legacy);

        Assert.Equal(SaveDecodeStatus.Malformed, decoded.Status);
        Assert.Null(decoded.Save);
    }

    [Fact]
    public void V1PayloadWithoutOptionalFields_StillMigrates()
    {
        SaveDecodeResult decoded = ProgressSavePolicy.Decode("""{"schemaVersion":1}""");

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(ContentIds.ToolGrab, decoded.Save!.SelectedToolId);
        Assert.Equal([ContentIds.ToolGrab], decoded.Save.UnlockedToolIds);
        Assert.Equal(0.0f, decoded.Save.Mood);
    }

    /// <summary>
    /// "This build cannot activate that selection" covers a known tool that is simply not
    /// unlocked, so the original value is retained rather than silently discarded.
    /// </summary>
    [Fact]
    public void KnownButLockedSelection_FallsBackToGrabAndIsRetained()
    {
        var save = new ProgressSave
        {
            UnlockedToolIds = [ContentIds.ToolGrab],
            SelectedToolId = ContentIds.ToolBoxingGlove,
        };

        BuddyProgressState state = ProgressSavePolicy.CreateState(save, CashPerPain);

        Assert.Equal(ToolId.Grab, state.SelectedTool);
        Assert.Equal(ContentIds.ToolBoxingGlove, state.Extensions!.UnknownSelectedToolId);
    }

    [Fact]
    public void ProgressAndLocalSettings_AreSeparateDtos()
    {
        string progress = ProgressSavePolicy.Serialize(new ProgressSave());
        string settings = JsonSerializer.Serialize(new LocalSettingsSave
        {
            WindowX = 123,
            WindowY = 456,
            AlwaysOnTop = false,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("windowX", progress);
        Assert.DoesNotContain("alwaysOnTop", progress);
        Assert.Contains("\"windowX\":123", settings);
        Assert.Contains("\"alwaysOnTop\":false", settings);
    }

    [Fact]
    public void APaidStrengthUpgradeBecomesThePowerGrabTool()
    {
        // FR-019.9: the passive upgrade was never implemented, so the player who bought it
        // owns the tool that replaced it — not a refund, not a silently dropped purchase.
        string legacy = Downgrade(
            ProgressSavePolicy.Serialize(new ProgressSave
            {
                Revision = 11,
                BalanceMilliCredits = 4_200,
                UnlockedToolIds = [ContentIds.ToolGrab, ContentIds.UpgradeStrength],
                SelectedToolId = ContentIds.ToolGrab,
                Statistics = new ProgressStatisticsSave { ScoredImpacts = 17 },
                Times = new CumulativeTimesSave { RunSeconds = 90.0 },
            }),
            5);

        SaveDecodeResult decoded = ProgressSavePolicy.Decode(legacy);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(ProgressSave.CurrentSchemaVersion, decoded.Save!.SchemaVersion);
        Assert.Contains(ContentIds.ToolPowerGrab, decoded.Save.UnlockedToolIds);
        Assert.DoesNotContain(ContentIds.UpgradeStrength, decoded.Save.UnlockedToolIds);
        Assert.Contains(ContentIds.ToolGrab, decoded.Save.UnlockedToolIds);

        // Everything the migration does not name passes through untouched.
        Assert.Equal(11, decoded.Save.Revision);
        Assert.Equal(4_200, decoded.Save.BalanceMilliCredits);
        Assert.Equal(ContentIds.ToolGrab, decoded.Save.SelectedToolId);
        Assert.Equal(17, decoded.Save.Statistics.ScoredImpacts);
        Assert.Equal(90.0, decoded.Save.Times.RunSeconds);
    }

    [Fact]
    public void ASaveThatNeverBoughtTheUpgradeIsNotGrantedPowerGrab()
    {
        string legacy = Downgrade(
            ProgressSavePolicy.Serialize(new ProgressSave
            {
                UnlockedToolIds = [ContentIds.ToolGrab, ContentIds.ToolPet],
            }),
            5);

        ProgressSave migrated = ProgressSavePolicy.Decode(legacy).Save!;

        Assert.DoesNotContain(ContentIds.ToolPowerGrab, migrated.UnlockedToolIds);
        Assert.Equal(
            [ContentIds.ToolGrab, ContentIds.ToolPet],
            migrated.UnlockedToolIds);
    }

    [Fact]
    public void MigratingASaveThatAlreadyOwnsBothGrantsPowerGrabExactlyOnce()
    {
        string legacy = Downgrade(
            ProgressSavePolicy.Serialize(new ProgressSave
            {
                UnlockedToolIds =
                [
                    ContentIds.ToolGrab,
                    ContentIds.UpgradeStrength,
                    ContentIds.ToolPowerGrab,
                ],
            }),
            5);

        ProgressSave migrated = ProgressSavePolicy.Decode(legacy).Save!;

        Assert.Single(migrated.UnlockedToolIds, ContentIds.ToolPowerGrab);
        Assert.DoesNotContain(ContentIds.UpgradeStrength, migrated.UnlockedToolIds);
    }

    [Fact]
    public void ACurrentSaveNeverEmitsTheRetiredUpgradeId()
    {
        string json = ProgressSavePolicy.Serialize(new ProgressSave
        {
            UnlockedToolIds = [ContentIds.ToolGrab, ContentIds.ToolPowerGrab],
        });

        Assert.DoesNotContain(ContentIds.UpgradeStrength, json);
        Assert.Contains(ContentIds.ToolPowerGrab, json);
    }

    /// <summary>
    /// Rewrites a current-schema payload's version stamp so a migration can be exercised
    /// without committing a fixture that goes stale the next time a field is added.
    /// </summary>
    private static string Downgrade(string json, int schemaVersion) => json
        .Replace(
            $"\"schemaVersion\": {ProgressSave.CurrentSchemaVersion}",
            $"\"schemaVersion\": {schemaVersion}")
        .Replace(
            $"\"schemaVersion\":{ProgressSave.CurrentSchemaVersion}",
            $"\"schemaVersion\":{schemaVersion}");
}

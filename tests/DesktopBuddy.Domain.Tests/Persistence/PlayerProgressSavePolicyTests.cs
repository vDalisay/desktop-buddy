using System;
using System.Collections.Generic;
using System.Text.Json;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class PlayerProgressSavePolicyTests
{
    [Fact]
    public void Player_progress_round_trip_preserves_account_only_state()
    {
        var statistics = new ProgressStatistics(
            ScoredImpacts: 12,
            Knockouts: 3,
            CareAwards: 7,
            TrustResets: 2,
            EarnedMilliCredits: 45_000,
            SuccessfulCatches: 4,
            TotalPainMilli: 8_500,
            BestOneSecondMilliCredits: 3_000,
            BestThreeSecondMilliCredits: 5_000,
            BestTenSecondMilliCredits: 9_000,
            HighestMood: 42.0f,
            LowestMood: -31.0f,
            ToolUses: new Dictionary<string, long>(StringComparer.Ordinal)
            {
                [ContentIds.ToolPistol] = 9,
            },
            ToolPainMilli: new Dictionary<string, long>(StringComparer.Ordinal)
            {
                [ContentIds.ToolPistol] = 8_500,
            });
        var snapshot = new PlayerProgressSnapshot(
            Revision: 17,
            BalanceMilliCredits: 123_456,
            SelectedToolId: ContentIds.ToolPistol,
            UnlockedContentIds: [ContentIds.ToolGrab, ContentIds.ToolPistol],
            Statistics: statistics,
            Times: new CumulativeTimes(100.0, 80.0, 20.0),
            Extensions: new ProgressExtensionData(
                UnknownSelectedToolId: null,
                UnknownContentIds: ["future.content"],
                Values: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["feature.test"] = "v1",
                }));
        var state = new PlayerProgressState(cashPerPain: 0.01, snapshot);

        string json = PlayerProgressSavePolicy.Serialize(state);
        PlayerProgressDecodeResult decoded = PlayerProgressSavePolicy.Decode(json);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        PlayerProgressSnapshot restored = Assert.IsType<PlayerProgressSnapshot>(decoded.Snapshot);
        Assert.Equal(17, restored.Revision);
        Assert.Equal(123_456, restored.BalanceMilliCredits);
        Assert.Equal(ContentIds.ToolPistol, restored.SelectedToolId);
        Assert.Contains(ContentIds.ToolGrab, restored.UnlockedContentIds);
        Assert.Contains(ContentIds.ToolPistol, restored.UnlockedContentIds);
        Assert.Equal(statistics.ScoredImpacts, restored.Statistics.ScoredImpacts);
        Assert.Equal(statistics.Knockouts, restored.Statistics.Knockouts);
        Assert.Equal(statistics.CareAwards, restored.Statistics.CareAwards);
        Assert.Equal(statistics.TrustResets, restored.Statistics.TrustResets);
        Assert.Equal(statistics.EarnedMilliCredits, restored.Statistics.EarnedMilliCredits);
        Assert.Equal(statistics.TotalPainMilli, restored.Statistics.TotalPainMilli);
        Assert.Equal(9, restored.Statistics.ToolUses![ContentIds.ToolPistol]);
        Assert.Equal(8_500, restored.Statistics.ToolPainMilli![ContentIds.ToolPistol]);
        Assert.Equal(new CumulativeTimes(100.0, 80.0, 20.0), restored.Times);
        Assert.Equal("v1", restored.Extensions!.Values!["feature.test"]);
        Assert.Contains("future.content", restored.Extensions.UnknownContentIds!);

        // Mood extrema remain valid account-wide lifetime statistics. What must never appear in
        // this document are the live Buddy-local state fields themselves at the account root.
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.False(root.TryGetProperty("mood", out _));
        Assert.False(root.TryGetProperty("fullness", out _));
        Assert.False(root.TryGetProperty("harmfulContentIds", out _));
        Assert.False(root.TryGetProperty("funActivities", out _));
        Assert.False(root.TryGetProperty("buddyIdentityId", out _));
        Assert.False(root.TryGetProperty("characterId", out _));
    }

    [Fact]
    public void Selected_tool_must_be_owned_and_grab_must_always_be_owned()
    {
        PlayerProgressSave selectedLocked = ValidSave() with
        {
            SelectedToolId = ContentIds.ToolPistol,
            UnlockedContentIds = [ContentIds.ToolGrab],
        };
        PlayerProgressSave missingGrab = ValidSave() with
        {
            SelectedToolId = ContentIds.ToolPistol,
            UnlockedContentIds = [ContentIds.ToolPistol],
        };

        AssertInvalid(selectedLocked);
        AssertInvalid(missingGrab);
    }

    [Fact]
    public void Duplicate_or_blank_unlocks_are_invalid()
    {
        PlayerProgressSave duplicate = ValidSave() with
        {
            UnlockedContentIds = [ContentIds.ToolGrab, ContentIds.ToolGrab],
        };
        PlayerProgressSave blank = ValidSave() with
        {
            UnlockedContentIds = [ContentIds.ToolGrab, ""],
        };

        AssertInvalid(duplicate);
        AssertInvalid(blank);
    }

    [Fact]
    public void Negative_account_values_and_invalid_statistics_are_rejected()
    {
        AssertInvalid(ValidSave() with { Revision = -1 });
        AssertInvalid(ValidSave() with { BalanceMilliCredits = -1 });
        AssertInvalid(ValidSave() with
        {
            Statistics = new ProgressStatisticsSave { ScoredImpacts = -1 },
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Malformed_player_payloads_are_reported_as_malformed(string json)
    {
        Assert.Equal(SaveDecodeStatus.Malformed, PlayerProgressSavePolicy.Decode(json).Status);
    }

    [Fact]
    public void Future_player_schema_is_unsupported_not_treated_as_corruption()
    {
        PlayerProgressDecodeResult decoded = PlayerProgressSavePolicy.Decode("{\"schemaVersion\":999}");

        Assert.Equal(SaveDecodeStatus.UnsupportedFutureVersion, decoded.Status);
        Assert.Null(decoded.Snapshot);
    }

    private static PlayerProgressSave ValidSave() => new()
    {
        Revision = 0,
        BalanceMilliCredits = 0,
        SelectedToolId = ContentIds.ToolGrab,
        UnlockedContentIds = [ContentIds.ToolGrab],
        Statistics = new ProgressStatisticsSave(),
        Times = new CumulativeTimesSave(),
        Extensions = null,
    };

    private static void AssertInvalid(PlayerProgressSave save)
    {
        string json = JsonSerializer.Serialize(
            save,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(SaveDecodeStatus.Invalid, PlayerProgressSavePolicy.Decode(json).Status);
    }
}

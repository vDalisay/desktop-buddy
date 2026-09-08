using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class BuddyIdentitySavePolicyTests
{
    [Fact]
    public void Buddy_identity_round_trip_preserves_identity_traits_hunger_memory_and_novelty()
    {
        BuddyIdentityId id = BuddyIdentityId.From(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        Guid character = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var traits = new BuddyTraits(73, new FunPreferences(2, 7, 13, 19));
        var state = new BuddyIdentityState(new BuddyIdentitySnapshot(
            id,
            Revision: 12,
            CharacterId: character,
            Mood: -18.5f,
            Fullness: 62.0f,
            HarmfulContentIds: [ContentIds.ToolPistol, ContentIds.ToolGrenade],
            Traits: traits,
            FunInterest:
            [
                new FunActivityInterest(FunActivityId.Catch, 91.0f, false),
                new FunActivityInterest(FunActivityId.Pet, 44.0f, false),
                new FunActivityInterest(FunActivityId.Tickle, 0.0f, true),
                new FunActivityInterest(FunActivityId.Treat, 25.0f, true),
            ]));

        string json = BuddyIdentitySavePolicy.Serialize(state);
        BuddyIdentityDecodeResult decoded = BuddyIdentitySavePolicy.Decode(json);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        BuddyIdentityState restored = Assert.IsType<BuddyIdentityState>(decoded.State);
        Assert.Equal(id, restored.BuddyIdentityId);
        Assert.Equal(character, restored.CharacterId);
        Assert.Equal(12, restored.Revision);
        Assert.Equal(-18.5f, restored.Mood);
        Assert.Equal(62.0f, restored.Fullness);
        Assert.Equal(traits, restored.Traits);
        Assert.True(restored.IsContentHarmful(ContentIds.ToolPistol));
        Assert.True(restored.IsContentHarmful(ContentIds.ToolGrenade));
        Assert.Equal(91.0f, restored.InterestIn(FunActivityId.Catch));
        Assert.Equal(44.0f, restored.InterestIn(FunActivityId.Pet));
        Assert.Equal(0.0f, restored.InterestIn(FunActivityId.Tickle));
        Assert.Equal(25.0f, restored.InterestIn(FunActivityId.Treat));
        Assert.False(restored.IsFun(FunActivityId.Tickle));
    }

    [Fact]
    public void Missing_or_duplicate_fun_activity_is_invalid_not_defaulted_silently()
    {
        string missing = $$"""
        {
          "schemaVersion": 1,
          "buddyIdentityId": "{{Guid.Parse("11111111-2222-3333-4444-555555555555")}}",
          "revision": 0,
          "mood": 0,
          "fullness": 50,
          "harmfulContentIds": [],
          "obstacleHopPropensity": 50,
          "funActivities": [
            { "activityId": "fun.catch", "drain": 5, "interest": 100, "bored": false },
            { "activityId": "fun.pet", "drain": 5, "interest": 100, "bored": false },
            { "activityId": "fun.tickle", "drain": 5, "interest": 100, "bored": false }
          ]
        }
        """;
        string duplicate = missing.Replace(
            "]\n}",
            ",\n    { \"activityId\": \"fun.tickle\", \"drain\": 5, \"interest\": 100, \"bored\": false }\n  ]\n}",
            StringComparison.Ordinal);

        Assert.Equal(SaveDecodeStatus.Invalid, BuddyIdentitySavePolicy.Decode(missing).Status);
        Assert.Equal(SaveDecodeStatus.Invalid, BuddyIdentitySavePolicy.Decode(duplicate).Status);
    }

    [Theory]
    [InlineData(-101.0f, 50.0f)]
    [InlineData(101.0f, 50.0f)]
    [InlineData(0.0f, -1.0f)]
    [InlineData(0.0f, 101.0f)]
    public void Invalid_mood_or_fullness_is_rejected(float mood, float fullness)
    {
        BuddyIdentitySave save = ValidSave() with { Mood = mood, Fullness = fullness };

        string json = System.Text.Json.JsonSerializer.Serialize(save, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal(SaveDecodeStatus.Invalid, BuddyIdentitySavePolicy.Decode(json).Status);
    }

    [Fact]
    public void Invalid_empty_ids_and_duplicate_harmful_memory_are_rejected()
    {
        BuddyIdentitySave emptyId = ValidSave() with { BuddyIdentityId = Guid.Empty };
        BuddyIdentitySave emptyCharacter = ValidSave() with { CharacterId = Guid.Empty };
        BuddyIdentitySave duplicateHarm = ValidSave() with
        {
            HarmfulContentIds = [ContentIds.ToolPistol, ContentIds.ToolPistol],
        };

        AssertInvalid(emptyId);
        AssertInvalid(emptyCharacter);
        AssertInvalid(duplicateHarm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Malformed_identity_payloads_are_reported_as_malformed(string json)
    {
        Assert.Equal(SaveDecodeStatus.Malformed, BuddyIdentitySavePolicy.Decode(json).Status);
    }

    [Fact]
    public void Future_identity_schema_is_unsupported_not_treated_as_corruption()
    {
        BuddyIdentityDecodeResult decoded = BuddyIdentitySavePolicy.Decode("{\"schemaVersion\":999}");

        Assert.Equal(SaveDecodeStatus.UnsupportedFutureVersion, decoded.Status);
        Assert.Null(decoded.State);
    }

    private static BuddyIdentitySave ValidSave() => new()
    {
        BuddyIdentityId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Revision = 0,
        CharacterId = null,
        Mood = 0.0f,
        Fullness = 50.0f,
        HarmfulContentIds = [],
        ObstacleHopPropensity = 50,
        FunActivities =
        [
            new BuddyIdentityFunSave { ActivityId = ContentIds.FunCatch, Drain = 5, Interest = 100.0f },
            new BuddyIdentityFunSave { ActivityId = ContentIds.FunPet, Drain = 5, Interest = 100.0f },
            new BuddyIdentityFunSave { ActivityId = ContentIds.FunTickle, Drain = 5, Interest = 100.0f },
            new BuddyIdentityFunSave { ActivityId = ContentIds.FunTreat, Drain = 5, Interest = 100.0f },
        ],
    };

    private static void AssertInvalid(BuddyIdentitySave save)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(save, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Equal(SaveDecodeStatus.Invalid, BuddyIdentitySavePolicy.Decode(json).Status);
    }
}

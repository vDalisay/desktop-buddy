using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Serialization;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

/// <summary>
/// The persisted types are serialized through a source-generated context so the NativeAOT build can
/// read its own saves. These are save-format guards, not serializer tests: the generated metadata
/// has to produce exactly what the reflection-based serializer produced, or shipping the change
/// silently rewrites every player's file.
/// </summary>
public sealed class SourceGeneratedSerializationTests
{
    /// <summary>The options each store uses, minus the generated resolver.</summary>
    private static JsonSerializerOptions Reflection() =>
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static JsonSerializerOptions SourceGenerated() =>
        new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            TypeInfoResolver = DomainJsonContext.Default,
        };

    private static ProgressSave PopulatedSave() => new()
    {
        SchemaVersion = ProgressSave.CurrentSchemaVersion,
        Revision = 1674207,
        BalanceMilliCredits = 387691442,
        // The property that first broke the NativeAOT build: a nullable value type needs a
        // generic instantiation that only exists if it was generated ahead of time.
        ActiveCharacterId = Guid.Parse("6f3d2c5a-1b7e-4a90-9c11-55d0f2a4e8b3"),
        UnlockedToolIds = new List<string> { "cosmetic.brows.bushy", "tool.grab" },
    };

    [Fact]
    public void ProgressSave_SourceGeneratedOutput_MatchesReflectionByteForByte()
    {
        ProgressSave save = PopulatedSave();

        string reflection = JsonSerializer.Serialize(save, Reflection());
        string generated = JsonSerializer.Serialize(save, SourceGenerated());

        Assert.Equal(reflection, generated);
    }

    [Fact]
    public void ProgressSave_NullableGuid_RoundTripsThroughTheGeneratedContext()
    {
        ProgressSave save = PopulatedSave();

        string json = JsonSerializer.Serialize(save, SourceGenerated());
        ProgressSave? restored = JsonSerializer.Deserialize<ProgressSave>(json, SourceGenerated());

        Assert.NotNull(restored);
        Assert.Equal(save.ActiveCharacterId, restored!.ActiveCharacterId);
        Assert.Equal(save.BalanceMilliCredits, restored.BalanceMilliCredits);
        Assert.Equal(save.UnlockedToolIds, restored.UnlockedToolIds);
    }

    [Fact]
    public void ProgressSave_AbsentNullableGuid_StaysAbsent()
    {
        ProgressSave save = PopulatedSave() with { ActiveCharacterId = null };

        string reflection = JsonSerializer.Serialize(save, Reflection());
        string generated = JsonSerializer.Serialize(save, SourceGenerated());

        Assert.Equal(reflection, generated);
        Assert.Null(JsonSerializer.Deserialize<ProgressSave>(generated, SourceGenerated())!.ActiveCharacterId);
    }

    [Fact]
    public void ProgressSave_UnknownFields_SurviveTheGeneratedContext()
    {
        // Forward compatibility rides on [JsonExtensionData]; a generated context that dropped it
        // would silently delete a newer build's fields on the next save.
        const string json = """
            {
              "schemaVersion": 8,
              "revision": 7,
              "balanceMilliCredits": 1234,
              "aFieldFromANewerBuild": { "nested": true }
            }
            """;

        ProgressSave? restored = JsonSerializer.Deserialize<ProgressSave>(json, SourceGenerated());

        Assert.NotNull(restored);
        Assert.NotNull(restored!.UnknownFields);
        Assert.True(restored.UnknownFields!.ContainsKey("aFieldFromANewerBuild"));
        Assert.Contains("aFieldFromANewerBuild", JsonSerializer.Serialize(restored, SourceGenerated()));
    }

    [Fact]
    public void ProgressSavePolicy_RoundTripsItsOwnOutput()
    {
        // The policy owns the real options the game ships with, resolver included.
        ProgressSave save = PopulatedSave();

        string json = ProgressSavePolicy.Serialize(save);
        SaveDecodeResult restored = ProgressSavePolicy.Decode(json);

        Assert.NotNull(restored.Save);
        Assert.Equal(save.BalanceMilliCredits, restored.Save!.BalanceMilliCredits);
        Assert.Equal(save.ActiveCharacterId, restored.Save.ActiveCharacterId);
    }

    [Fact]
    public void LocalSettingsSave_SourceGeneratedOutput_MatchesReflectionByteForByte()
    {
        var settings = new LocalSettingsSave { SchemaVersion = 1 };

        Assert.Equal(
            JsonSerializer.Serialize(settings, Reflection()),
            JsonSerializer.Serialize(settings, SourceGenerated()));
    }
}

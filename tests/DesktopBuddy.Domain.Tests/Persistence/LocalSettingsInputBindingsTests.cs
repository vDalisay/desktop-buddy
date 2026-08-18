using System;
using System.Collections.Generic;
using System.Text.Json;
using DesktopBuddy.Domain.Persistence;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Persistence;

public sealed class LocalSettingsInputBindingsTests
{
    [Fact]
    public void DropTool_DefaultsToDWhenOlderSettingsHaveNoBinding()
    {
        Assert.Equal(
            LocalSettingsInputBindings.DefaultDropTool,
            LocalSettingsInputBindings.DropTool(new LocalSettingsSave()));
    }

    [Fact]
    public void DropTool_RoundTripsThroughExtensionDataWithoutDiscardingUnknownFields()
    {
        var settings = new LocalSettingsSave
        {
            UnknownFields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["futureSetting"] = JsonSerializer.SerializeToElement(42),
            },
        };

        LocalSettingsSave edited = LocalSettingsInputBindings.WithDropTool(settings, "Shift+G");
        string json = JsonSerializer.Serialize(edited);
        LocalSettingsSave restored = JsonSerializer.Deserialize<LocalSettingsSave>(json)!;

        Assert.Equal("Shift+G", LocalSettingsInputBindings.DropTool(restored));
        Assert.Equal(42, restored.UnknownFields!["futureSetting"].GetInt32());
        Assert.Equal(JsonValueKind.String, restored.UnknownFields[LocalSettingsInputBindings.DropToolField].ValueKind);
    }
}

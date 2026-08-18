using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DesktopBuddy.Domain.Persistence;

/// <summary>
/// Backward-compatible machine-local input bindings stored through LocalSettingsSave's extension
/// data. This lets the Demo add rebindable actions without a settings-schema bump or discarding
/// unknown settings written by future builds.
/// </summary>
public static class LocalSettingsInputBindings
{
    public const string DropToolField = "dropToolHotkey";
    public const string DefaultDropTool = "D";

    public static string DropTool(LocalSettingsSave settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.UnknownFields is { } fields &&
            fields.TryGetValue(DropToolField, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString()!;
        }
        return DefaultDropTool;
    }

    public static LocalSettingsSave WithDropTool(LocalSettingsSave settings, string chord)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(chord))
            throw new ArgumentException("Drop Tool hotkey cannot be blank.", nameof(chord));

        var fields = settings.UnknownFields is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(settings.UnknownFields, StringComparer.Ordinal);
        fields[DropToolField] = JsonSerializer.SerializeToElement(chord.Trim());
        return settings with { UnknownFields = fields };
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Backward-compatible machine-local window/input settings. Layout data is stored in the
/// existing extension map so older schema-1 settings remain readable and unknown fields are
/// preserved. The existing window rectangle always represents the compact layout.
/// </summary>
public static class WindowInteractionSettings
{
    public const string LayoutKey = "windowLayoutMode";
    public const string FullscreenMonitorKey = "fullscreenMonitor";

    public static WindowLayoutMode ReadLayout(LocalSettingsSave settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.UnknownFields is not null &&
            settings.UnknownFields.TryGetValue(LayoutKey, out JsonElement value) &&
            value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), "fullscreen-overlay", StringComparison.OrdinalIgnoreCase))
        {
            return WindowLayoutMode.FullscreenOverlay;
        }

        return WindowLayoutMode.Compact;
    }

    public static int ReadFullscreenMonitor(LocalSettingsSave settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.UnknownFields is not null &&
            settings.UnknownFields.TryGetValue(FullscreenMonitorKey, out JsonElement value) &&
            value.TryGetInt32(out int monitor) && monitor >= 0)
        {
            return monitor;
        }

        return Math.Max(0, settings.Monitor);
    }

    public static InputMode ReadInputMode(LocalSettingsSave settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // A pinned startup mode wins over the remembered one; "remember" falls through.
        string mode = settings.StartupInputMode switch
        {
            "work" => "work",
            "play" => "play",
            _ => settings.LastInputMode,
        };
        return string.Equals(mode, "play", StringComparison.OrdinalIgnoreCase)
            ? InputMode.Play
            : InputMode.Work;
    }

    public static Rect2I CompactRect(LocalSettingsSave settings) => new(
        settings.WindowX,
        settings.WindowY,
        settings.WindowWidth,
        settings.WindowHeight);

    public static LocalSettingsSave WithState(
        LocalSettingsSave settings,
        WindowLayoutMode layout,
        InputMode inputMode,
        Rect2I compactRect,
        int fullscreenMonitor)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var extensions = settings.UnknownFields is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(settings.UnknownFields, StringComparer.Ordinal);
        extensions[LayoutKey] = JsonSerializer.SerializeToElement(
            layout == WindowLayoutMode.FullscreenOverlay ? "fullscreen-overlay" : "compact");
        extensions[FullscreenMonitorKey] = JsonSerializer.SerializeToElement(
            Math.Max(0, fullscreenMonitor));

        return settings with
        {
            Revision = settings.Revision + 1,
            WindowX = compactRect.Position.X,
            WindowY = compactRect.Position.Y,
            WindowWidth = compactRect.Size.X,
            WindowHeight = compactRect.Size.Y,
            Monitor = Math.Max(0, fullscreenMonitor),
            LastInputMode = inputMode == InputMode.Play ? "play" : "work",
            UnknownFields = extensions,
        };
    }
}

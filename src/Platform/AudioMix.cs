using System;
using DesktopBuddy.Domain.Persistence;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// The three mixer buses the settings sliders drive. Gameplay audio routes to
/// <see cref="Sfx"/> and the UI feedback cues to <see cref="Ui"/>, so neither slider can move
/// the other and Master stays the overall level.
/// </summary>
public static class AudioMix
{
    public const string Master = "Master";
    public const string Sfx = "SFX";
    public const string Ui = "UI";

    /// <summary>Below this a slider means off, not "very quiet": linear-to-dB has no zero.</summary>
    private const float SilenceThreshold = 0.005f;

    /// <summary>
    /// Applies the three sliders. <paramref name="silenceAll"/> mutes the master bus without
    /// touching any slider, so Work Mode silence gives the exact mix back on the way out.
    /// </summary>
    public static void Apply(LocalSettingsSave settings, bool silenceAll = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SetBusVolume(Master, silenceAll ? 0.0f : settings.MasterVolume);
        SetBusVolume(Sfx, settings.SfxVolume);
        SetBusVolume(Ui, settings.UiVolume);
    }

    private static void SetBusVolume(string bus, float linear)
    {
        int index = AudioServer.GetBusIndex(bus);
        if (index < 0)
            return;

        float clamped = Math.Clamp(linear, 0.0f, 1.0f);
        bool silent = clamped < SilenceThreshold;
        AudioServer.SetBusMute(index, silent);
        if (!silent)
            AudioServer.SetBusVolumeDb(index, Mathf.LinearToDb(clamped));
    }
}

using System;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.UI.Win98;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Platform;

public partial class DesktopShellController
{
    /// <summary>
    /// The one seam the Settings rows change machine-local settings through: edit the record,
    /// apply everything that can be applied live, and hand the result back. Dragging a slider
    /// applies without persisting; the write happens through
    /// <see cref="SavePresentationSettingsAsync"/> once the control is released.
    /// </summary>
    public LocalSettingsSave EditSettings(Func<LocalSettingsSave, LocalSettingsSave> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        LocalSettingsSave edited = edit(_settings) ?? _settings;
        _settings = Sanitize(edited) with
        {
            Revision = _settings.Revision == long.MaxValue ? long.MaxValue : _settings.Revision + 1,
        };
        ApplyPresentationSettings();
        return _settings;
    }

    /// <summary>Persists whatever the Settings rows currently hold.</summary>
    public async Task SavePresentationSettingsAsync()
    {
        if (_saves is null)
            return;

        _saves.RegisterSettings(_settings);
        await _saves.SaveRegisteredSettingsAsync();
    }

    /// <summary>Applies every machine-local setting that can change without a restart.</summary>
    private void ApplyPresentationSettings()
    {
        ApplyAudioSettings();
        Win98ThemeFactory.ApplyScale(_settings.UiScalePercent / 100.0f);
        Win98ThemeFactory.ApplyPalette(Win98Palette.Parse(
            _settings.UiFaceColor, _settings.UiBarColor, _settings.UiTextColor));
        Window.ApplyFrameSettings(_settings.VSync, _settings.MaxFps);
        Window.SetAlwaysOnTop(_settings.AlwaysOnTop);
        ApplyZoom(_settings.ZoomPercent / 100.0);
    }

    /// <summary>
    /// The legacy broad Work Mode mute remains available independently from the narrower
    /// MuteWorkTyping preference. It mutes the whole mix without changing slider positions,
    /// while Work typing itself reads its dedicated setting at playback time.
    /// </summary>
    internal void ApplyAudioSettings() =>
        AudioMix.Apply(_settings, silenceAll: _settings.MuteInWorkMode && Mode == DomainInputMode.Work);

    /// <summary>
    /// Buddy Size and UI Scale both change the room: the first is the camera zoom, the second
    /// changes how much of the client box the frame chrome eats. Only zoom used to re-request
    /// the layout, so raising UI Scale alone left the walls where the smaller chrome had put
    /// them (owner report 2026-09-06).
    /// </summary>
    private void ApplyZoom(double zoom)
    {
        float chrome = UI.Win98.Win98ThemeFactory.ScaledChromeHeight;
        if (Math.Abs(zoom - _storedZoom) < 0.001 && Math.Abs(chrome - _storedChromeHeight) < 0.5f)
            return;

        _storedZoom = zoom;
        _storedChromeHeight = chrome;
        Boundaries.RequestLayout(RoomSizeFor(ResolveClientSize()), _storedZoom);
    }

    /// <summary>
    /// Rejects values the save validator would throw on, so one bad row can never make the
    /// settings file unwritable.
    /// </summary>
    private static LocalSettingsSave Sanitize(LocalSettingsSave settings) => settings with
    {
        MasterVolume = Clamp01(settings.MasterVolume),
        SfxVolume = Clamp01(settings.SfxVolume),
        UiVolume = Clamp01(settings.UiVolume),
        MaxFps = Math.Clamp(settings.MaxFps, 0, 480),
        BackgroundMaxFps = Math.Clamp(settings.BackgroundMaxFps, 0, 480),
        ZoomPercent = settings.ZoomPercent is 75 or 100 or 125 or 150 or 175 or 200
            ? settings.ZoomPercent
            : 100,
        UiScalePercent = settings.UiScalePercent is 100 or 125 or 150 or 175 or 200
            ? settings.UiScalePercent
            : 100,
        StartupInputMode = settings.StartupInputMode is "work" or "play" or "remember"
            ? settings.StartupInputMode
            : "remember",
    };

    private static float Clamp01(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.0f, 1.0f) : 1.0f;
}

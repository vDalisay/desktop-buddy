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
        Window.ApplyFrameSettings(_settings.VSync, _settings.MaxFps);
        Window.SetAlwaysOnTop(_settings.AlwaysOnTop);
        ApplyZoom(_settings.ZoomPercent / 100.0);
    }

    /// <summary>
    /// Work Mode silence is a mute of the whole mix rather than a volume change, so the sliders
    /// keep their positions and come back exactly where the player left them.
    /// </summary>
    internal void ApplyAudioSettings() =>
        AudioMix.Apply(_settings, silenceAll: _settings.MuteInWorkMode && Mode == DomainInputMode.Work);

    private void ApplyZoom(double zoom)
    {
        if (Math.Abs(zoom - _storedZoom) < 0.001)
            return;

        _storedZoom = zoom;
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

using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// One policy seam for short modern presentation easing inside the Win98 visual language.
/// Reduced Motion is accessibility and therefore always wins over the aesthetic preference.
/// </summary>
public static class Win98MotionPolicy
{
    public static bool Allows(LocalSettingsSave settings) =>
        settings.ModernUiMotion && !settings.ReducedMotion;

    public static double Duration(LocalSettingsSave settings, double modernSeconds) =>
        Allows(settings) ? modernSeconds : 0.0;
}

namespace DesktopBuddy.App;

/// <summary>
/// Stable names of the input-map actions declared in <c>project.godot</c>.
/// The single input reader (InputCollector, Milestone 2) resolves these; using
/// constants keeps action names out of scattered string literals and lets
/// <see cref="StartupValidator"/> assert the map is present.
///
/// Mouse buttons are bound to <see cref="Primary"/>/<see cref="Secondary"/>;
/// <see cref="Reload"/> is <c>R</c>; <see cref="ToggleInputMode"/> is the
/// in-app <c>Ctrl+Shift+B</c> equivalent (the OS-global hotkey is owned by the
/// native Windows adapter, not the Godot input map).
/// </summary>
public static class InputActions
{
    public const string Primary = "buddy_primary";
    public const string Secondary = "buddy_secondary";
    public const string Reload = "buddy_reload";
    public const string ToggleInputMode = "toggle_input_mode";

    /// <summary>
    /// In-app hide-to-tray toggle (<c>Ctrl+Shift+H</c>). Hiding works from here;
    /// <b>restoring</b> a hidden window cannot, because Godot delivers no input to an
    /// invisible unfocused window — that path is the native tray icon / global hotkey
    /// scoped to Milestone 6.
    /// </summary>
    public const string ToggleHideToTray = "toggle_hide_to_tray";

    /// <summary>In-app Save &amp; Quit (<c>Ctrl+Shift+Q</c>): flush progress, then exit.</summary>
    public const string SaveAndQuit = "save_and_quit";

    public static readonly string[] All =
    {
        Primary,
        Secondary,
        Reload,
        ToggleInputMode,
        ToggleHideToTray,
        SaveAndQuit,
    };
}

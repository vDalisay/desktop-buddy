using System;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// The stored form of a rebindable chord ("Ctrl+Shift+B") and its translation to and from a
/// Godot key event. Only the in-app input map is rebound here; the OS-global hotkey remains the
/// native adapter's, per <see cref="InputActions"/>.
/// </summary>
public static class HotkeyBinding
{
    public const string Default = "Ctrl+Shift+B";

    public static string Format(InputEventKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        string text = string.Empty;
        if (key.CtrlPressed)
            text += "Ctrl+";
        if (key.ShiftPressed)
            text += "Shift+";
        if (key.AltPressed)
            text += "Alt+";
        return text + OS.GetKeycodeString(code);
    }

    /// <summary>Null for anything that is not a usable chord, including a bare modifier.</summary>
    public static InputEventKey? Parse(string? chord)
    {
        if (string.IsNullOrWhiteSpace(chord))
            return null;

        var key = new InputEventKey { Pressed = true };
        string[] parts = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            string part = parts[index];
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    key.CtrlPressed = true;
                    continue;
                case "shift":
                    key.ShiftPressed = true;
                    continue;
                case "alt":
                    key.AltPressed = true;
                    continue;
            }

            Key code = OS.FindKeycodeFromString(part);
            if (code == Key.None)
                return null;
            key.PhysicalKeycode = code;
        }

        return key.PhysicalKeycode == Key.None ? null : key;
    }

    /// <summary>Rebinds one action to the chord. Unparseable chords leave the map alone.</summary>
    public static bool Apply(string action, string? chord)
    {
        if (Parse(chord) is not { } key || !InputMap.HasAction(action))
            return false;

        InputMap.ActionEraseEvents(action);
        InputMap.ActionAddEvent(action, key);
        return true;
    }

    /// <summary>True for a key event that is a complete chord rather than a modifier being held.</summary>
    public static bool IsCompleteChord(InputEventKey key)
    {
        Key code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        return code is not (Key.None or Key.Ctrl or Key.Shift or Key.Alt or Key.Meta);
    }
}

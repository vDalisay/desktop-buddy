using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Shop;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Exercises the Settings hotkey capture state directly. Escape, bare modifiers, switching rows,
/// and closing the panel must never leak one shortcut into another or leave a row listening.
/// </summary>
public sealed class SettingsHotkeyCaptureScenario : IScenario
{
    public string Id => "settings_hotkey_capture";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var panel = new SettingsPanel { Visible = true };
        tree.Root.AddChild(panel);
        panel.Configure();

        string? workChanged = null;
        string? dropChanged = null;
        Button work = panel.AddHotkey("Work/Play Hotkey", "Switch modes.", "Ctrl+Shift+B", chord => workChanged = chord);
        Button drop = panel.AddHotkey("Drop Tool Hotkey", "Drop a tool.", "D", chord => dropChanged = chord);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        try
        {
            work.EmitSignal(BaseButton.SignalName.Pressed);
            panel._Input(new InputEventKey
            {
                Pressed = true,
                Keycode = Key.Ctrl,
                PhysicalKeycode = Key.Ctrl,
            });
            bool modifierKeepsListening = work.Text == "Press keys..." && workChanged is null;
            panel._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape, PhysicalKeycode = Key.Escape });
            bool firstCancelRestoresOwnChord = work.Text == "Ctrl+Shift+B" && workChanged is null;
            checks.Add(new StartupCheck(
                "settings_hotkey_modifier_waits_and_escape_restores_original",
                modifierKeepsListening && firstCancelRestoresOwnChord,
                $"modifier={modifierKeepsListening} restored={work.Text}"));

            work.EmitSignal(BaseButton.SignalName.Pressed);
            panel._Input(new InputEventKey
            {
                Pressed = true,
                CtrlPressed = true,
                Keycode = Key.K,
                PhysicalKeycode = Key.K,
            });
            bool workRebound = work.Text == "Ctrl+K" && workChanged == "Ctrl+K";

            drop.EmitSignal(BaseButton.SignalName.Pressed);
            panel._Input(new InputEventKey { Pressed = true, Keycode = Key.Escape, PhysicalKeycode = Key.Escape });
            bool secondCancelDoesNotLeakFirstChord = drop.Text == "D" && dropChanged is null && work.Text == "Ctrl+K";
            checks.Add(new StartupCheck(
                "settings_hotkey_cancel_is_isolated_per_row",
                workRebound && secondCancelDoesNotLeakFirstChord,
                $"work={work.Text}/{workChanged} drop={drop.Text}/{dropChanged}"));

            drop.EmitSignal(BaseButton.SignalName.Pressed);
            bool listeningBeforeClose = drop.Text == "Press keys...";
            panel.Visible = false;
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            bool closeCancelsCapture = listeningBeforeClose && drop.Text == "D" && dropChanged is null;
            checks.Add(new StartupCheck(
                "settings_hotkey_closing_panel_cancels_capture",
                closeCancelsCapture,
                $"before={listeningBeforeClose} after={drop.Text}"));
        }
        finally
        {
            panel.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }
}
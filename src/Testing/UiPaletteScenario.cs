using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Shop;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// The interface palette and its safety net. The rules that matter are the ones a player would
/// otherwise discover by being locked out: an applied palette is only kept if it is confirmed,
/// Return puts the old one back, the countdown running out does the same thing on its own, and
/// nothing unconfirmed is ever written down.
/// </summary>
public sealed class UiPaletteScenario : IScenario
{
    private static readonly Color Pink = Color.Color8(244, 194, 214);
    private static readonly Color Green = Color.Color8(24, 122, 64);
    private static readonly Color Cocoa = Color.Color8(64, 34, 20);

    public string Id => "ui_palette";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        Win98ThemeFactory.ApplyPalette(Win98Palette.Default);

        var host = new Control { Name = "UiPaletteScenarioHost" };
        tree.Root.AddChild(host);
        var panel = new SettingsPanel { Visible = true };
        host.AddChild(panel);
        panel.Configure();

        var saved = new List<Win98Palette>();
        var palette = new Win98PaletteSettings { Name = "ScenarioPaletteSettings" };
        host.AddChild(palette);
        palette.Compose(panel, host, Win98Palette.Default, saved.Add);
        await Frame(tree);

        try
        {
            Stage(panel, Pink, Green, Cocoa);
            bool stagedOnly = Win98ThemeFactory.Face == Win98Palette.Default.Face && saved.Count == 0;
            checks.Add(new StartupCheck(
                "picking_colors_changes_nothing_until_apply",
                stagedOnly,
                $"face={Win98ThemeFactory.Face.ToHtml(false)} saves={saved.Count}"));

            Press(panel, "Apply Colors");
            await Frame(tree);
            bool applied =
                Win98ThemeFactory.Face == Pink &&
                Win98ThemeFactory.ActiveTitle == Green &&
                Win98ThemeFactory.Dark == Cocoa &&
                palette.AwaitingConfirmation &&
                palette.SecondsRemaining == Win98PaletteSettings.ConfirmSeconds &&
                saved.Count == 0;
            checks.Add(new StartupCheck(
                "apply_shows_the_palette_and_asks_before_saving_it",
                applied,
                $"face={Win98ThemeFactory.Face.ToHtml(false)} bar={Win98ThemeFactory.ActiveTitle.ToHtml(false)} " +
                $"text={Win98ThemeFactory.Dark.ToHtml(false)} prompt={palette.AwaitingConfirmation} " +
                $"seconds={palette.SecondsRemaining} saves={saved.Count}"));

            // Derived shades follow the face rather than staying grey around a pink panel.
            bool derived =
                Win98ThemeFactory.Shadow != Color.Color8(128, 128, 128) &&
                Win98ThemeFactory.Highlight != Color.Color8(223, 223, 223) &&
                Win98ThemeFactory.Selection == Green;
            checks.Add(new StartupCheck(
                "bevels_and_selection_follow_the_chosen_colors",
                derived,
                $"shadow={Win98ThemeFactory.Shadow.ToHtml(false)} highlight={Win98ThemeFactory.Highlight.ToHtml(false)} " +
                $"selection={Win98ThemeFactory.Selection.ToHtml(false)}"));

            Press(panel, "Return");
            await Frame(tree);
            bool returned =
                Win98ThemeFactory.Palette.IsDefault &&
                !palette.AwaitingConfirmation &&
                saved.Count == 0;
            checks.Add(new StartupCheck(
                "return_puts_the_previous_palette_back_and_saves_nothing",
                returned,
                $"palette={Win98ThemeFactory.Face.ToHtml(false)} prompt={palette.AwaitingConfirmation} saves={saved.Count}"));

            Stage(panel, Pink, Green, Cocoa);
            Press(panel, "Apply Colors");
            await Frame(tree);
            RunOutCountdown(host);
            await Frame(tree);
            bool timedOut =
                Win98ThemeFactory.Palette.IsDefault &&
                !palette.AwaitingConfirmation &&
                saved.Count == 0;
            checks.Add(new StartupCheck(
                "letting_the_countdown_run_out_reverts_on_its_own",
                timedOut,
                $"palette={Win98ThemeFactory.Face.ToHtml(false)} prompt={palette.AwaitingConfirmation} saves={saved.Count}"));

            Stage(panel, Pink, Green, Cocoa);
            Press(panel, "Apply Colors");
            await Frame(tree);
            Press(panel, "Confirm");
            await Frame(tree);
            RunOutCountdown(host);
            await Frame(tree);
            bool kept =
                Win98ThemeFactory.Face == Pink &&
                !palette.AwaitingConfirmation &&
                saved.Count == 1 &&
                saved[0].Face == Pink &&
                saved[0].BarHex == "187a40";
            checks.Add(new StartupCheck(
                "confirm_keeps_the_palette_and_the_dead_timer_cannot_revert_it",
                kept,
                $"face={Win98ThemeFactory.Face.ToHtml(false)} saves={saved.Count} " +
                $"saved={(saved.Count > 0 ? saved[0].BarHex : "-")}"));

            Press(panel, "Default Colors");
            await Frame(tree);
            bool restored =
                Win98ThemeFactory.Palette.IsDefault &&
                Win98ThemeFactory.Light == Color.Color8(255, 255, 255) &&
                Win98ThemeFactory.HoverSelection == Color.Color8(72, 132, 208) &&
                saved.Count == 2 &&
                saved[1].IsDefault;
            checks.Add(new StartupCheck(
                "default_restores_the_shipped_grey_navy_and_black_exactly",
                restored,
                $"face={Win98ThemeFactory.Face.ToHtml(false)} light={Win98ThemeFactory.Light.ToHtml(false)} " +
                $"hover={Win98ThemeFactory.HoverSelection.ToHtml(false)} saves={saved.Count}"));

            Win98Palette recovered = Win98Palette.Parse("not a color", null, "#ff8800");
            checks.Add(new StartupCheck(
                "a_corrupt_stored_color_falls_back_instead_of_breaking_the_ui",
                recovered.Face == Win98Palette.Default.Face &&
                recovered.Bar == Win98Palette.Default.Bar &&
                recovered.TextHex == "ff8800",
                $"face={recovered.FaceHex} bar={recovered.BarHex} text={recovered.TextHex}"));
        }
        finally
        {
            Win98ThemeFactory.ApplyPalette(Win98Palette.Default);
            host.QueueFree();
            await Frame(tree);
        }

        return new ScenarioResult(
            checks.All(static check => check.Passed),
            checks,
            [$"seed={seed}"]);
    }

    /// <summary>Puts three colours in the pickers the way a player's clicks would.</summary>
    private static void Stage(SettingsPanel panel, Color face, Color bar, Color text)
    {
        Pick(panel, "Window Color", face);
        Pick(panel, "Bar Color", bar);
        Pick(panel, "Font Color", text);
    }

    private static void Pick(SettingsPanel panel, string row, Color color)
    {
        var swatch = (ColorPickerButton)panel.ControlFor(row)!;
        swatch.Color = color;
        swatch.EmitSignal(ColorPickerButton.SignalName.ColorChanged, color);
    }

    /// <summary>A Settings row's button, or one of the confirmation's own two.</summary>
    private static void Press(SettingsPanel panel, string row)
    {
        Button? button = panel.ActionFor(row);
        button ??= panel.GetTree().Root.FindChild($"UiPalette{row}Button", true, false) as Button;
        if (button is null)
            throw new System.InvalidOperationException($"No settings button for '{row}'.");
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    /// <summary>
    /// Fires the confirmation's own one-second timer to its end. Waiting ten real seconds would
    /// prove the same thing and cost the suite ten seconds per run.
    /// </summary>
    private static void RunOutCountdown(Control host)
    {
        if (host.FindChild("UiPaletteConfirmPrompt", true, false) is not Control prompt)
            return;
        foreach (Node child in prompt.FindChildren("*", nameof(Timer), true, false))
        {
            for (int tick = 0; tick < Win98PaletteSettings.ConfirmSeconds; tick++)
                ((Timer)child).EmitSignal(Timer.SignalName.Timeout);
        }
    }

    private static SignalAwaiter Frame(SceneTree tree) =>
        tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
}

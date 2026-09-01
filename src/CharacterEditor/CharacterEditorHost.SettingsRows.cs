using System;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Platform;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Every machine-local Settings row. The rows own no policy: they read the loaded settings for
/// their starting positions, hand each change to <see cref="DesktopShellController.EditSettings"/>
/// for applying, and ask for one durable write when the control is released.
/// </summary>
public partial class CharacterEditorHost
{
    private const string SoundGroup = "Sound";
    private const string DisplayGroup = "Display";
    private const string EffectsGroup = "Accessibility";
    private const string BehaviourGroup = "Startup and Behaviour";
    private const string DataGroup = "Saved Data";

    private static readonly int[] FrameLimits = [0, 30, 60, 120];
    private static readonly string[] FrameLimitLabels = ["V-Sync", "30", "60", "120"];
    private static readonly int[] BackgroundFrameLimits = [0, 5, 10, 30];
    private static readonly string[] BackgroundFrameLimitLabels = ["Default", "5", "10", "30"];
    private static readonly int[] UiScaleSteps = [100, 125, 150, 175, 200];
    private static readonly string[] UiScaleLabels = ["100%", "125%", "150%", "175%", "200%"];
    private static readonly int[] ZoomSteps = [75, 100, 125, 150, 175, 200];
    private static readonly string[] ZoomLabels = ["75%", "100%", "125%", "150%", "175%", "200%"];
    private static readonly string[] StartupModes = ["remember", "work", "play"];
    private static readonly string[] StartupModeLabels = ["Remember", "Work", "Play"];

    private PanelContainer? _resetPrompt;
    private UI.Win98.Win98PaletteSettings? _paletteSettings;

    private void ComposePresentationRows()
    {
        LocalSettingsSave settings = _sandbox.Shell.CurrentLocalSettings;
        void Save() => _ = _sandbox.Shell.SavePresentationSettingsAsync();
        void Edit(Func<LocalSettingsSave, LocalSettingsSave> edit)
        {
            _sandbox.Shell.EditSettings(edit);
            Save();
        }

        ComposeSoundRows(settings, Save, Edit);
        ComposeDisplayRows(settings, Edit);
        ComposeColorRows(settings);
        ComposeEffectsRows(settings, Edit);
        ComposeBehaviourRows(settings, Edit);
        ComposeDataRows();
    }

    private void ComposeSoundRows(
        LocalSettingsSave settings,
        Action save,
        Action<Func<LocalSettingsSave, LocalSettingsSave>> edit)
    {
        _settingsPanel.AddSlider(
            "Master Volume",
            "Controls every sound in the game. The volume sliders below are all relative to this one.",
            settings.MasterVolume,
            value => _sandbox.Shell.EditSettings(s => s with { MasterVolume = value }),
            save,
            SoundGroup);
        _settingsPanel.AddSlider(
            "Sound Effects",
            "Controls gameplay sounds such as hits, gunfire and explosions.",
            settings.SfxVolume,
            value => _sandbox.Shell.EditSettings(s => s with { SfxVolume = value }),
            save,
            SoundGroup);
        _settingsPanel.AddSlider(
            "Interface Sounds",
            "Controls menu clicks, button presses, purchase chimes and save confirmations.",
            settings.UiVolume,
            value => _sandbox.Shell.EditSettings(s => s with { UiVolume = value }),
            save,
            SoundGroup);
        _settingsPanel.AddToggle(
            "Mute While Working",
            "Mutes the game so your Buddy will not interrupt your work or calls.",
            settings.MuteInWorkMode,
            value => edit(s => s with { MuteInWorkMode = value }),
            SoundGroup);
        _settingsPanel.AddToggle(
            "Mute Work Typing",
            "Mutes only Buddy's keyboard sounds in Work Mode. Everything else stays audible.",
            settings.MuteWorkTyping,
            value => edit(s => s with { MuteWorkTyping = value }),
            SoundGroup);
    }

    private void ComposeDisplayRows(
        LocalSettingsSave settings,
        Action<Func<LocalSettingsSave, LocalSettingsSave>> edit)
    {
        _settingsPanel.AddToggle(
            "V-Sync",
            "Matches the frame rate to your monitor's refresh rate. Turning it off can reduce input lag, but may cause screen tearing.",
            settings.VSync,
            value => edit(s => s with { VSync = value }),
            DisplayGroup);
        _settingsPanel.AddChoice(
            "Frame Limit",
            "Sets the game's maximum frame rate.",
            FrameLimitLabels,
            Math.Max(0, Array.IndexOf(FrameLimits, settings.MaxFps)),
            index => edit(s => s with { MaxFps = FrameLimits[index] }),
            DisplayGroup);
        _settingsPanel.AddChoice(
            "Background Frame Limit",
            "Sets the frame limit while the game is hidden or in the background.",
            BackgroundFrameLimitLabels,
            Math.Max(0, Array.IndexOf(BackgroundFrameLimits, settings.BackgroundMaxFps)),
            index => edit(s =>
            {
                _sandbox.Lifecycle.BackgroundMaxFps = BackgroundFrameLimits[index];
                return s with { BackgroundMaxFps = BackgroundFrameLimits[index] };
            }),
            DisplayGroup);
        _settingsPanel.AddChoice(
            "UI Scale",
            "Changes the size of menus, buttons and text.",
            UiScaleLabels,
            Math.Max(0, Array.IndexOf(UiScaleSteps, settings.UiScalePercent)),
            index => edit(s => s with { UiScalePercent = UiScaleSteps[index] }),
            DisplayGroup);
        _settingsPanel.AddChoice(
            "Buddy Size",
            "Changes how large your Buddy and the room appear inside the window. It does not resize the window itself.",
            ZoomLabels,
            Math.Max(0, Array.IndexOf(ZoomSteps, settings.ZoomPercent)),
            index => edit(s => s with { ZoomPercent = ZoomSteps[index] }),
            DisplayGroup);
        _settingsPanel.AddToggle(
            "Modern UI Motion",
            "Adds short, smooth transitions to menus and previews.",
            settings.ModernUiMotion,
            value => edit(s => s with { ModernUiMotion = value }),
            DisplayGroup);
        _settingsPanel.AddToggle(
            "Work Mode Retro Filter",
            "Gives Buddy and the computer in Work Mode a chunky CRT look: coarser pixels, fewer colours and scanlines.",
            settings.WorkRetroFilter,
            value => edit(s => s with { WorkRetroFilter = value }),
            DisplayGroup);
        _settingsPanel.AddToggle(
            "Always On Top",
            "Keeps Buddy's window above your other windows.",
            settings.AlwaysOnTop,
            value => edit(s => s with { AlwaysOnTop = value }),
            DisplayGroup);

        int monitors = _sandbox.Window.MonitorCount;
        if (monitors > 1)
        {
            var labels = new string[monitors];
            for (int index = 0; index < monitors; index++)
                labels[index] = $"Monitor {index + 1}";
            _settingsPanel.AddChoice(
                "Monitor",
                "Chooses which display Buddy appears on.",
                labels,
                Math.Clamp(settings.Monitor, 0, monitors - 1),
                index =>
                {
                    _sandbox.Window.MoveToMonitor(index);
                    edit(s => s with { Monitor = index });
                },
                DisplayGroup);
        }
    }

    /// <summary>
    /// The interface palette rows. They persist only through the confirmation the controller
    /// runs, so an unconfirmed preview is never written down: whatever the settings file holds
    /// is a palette the player said yes to.
    /// </summary>
    private void ComposeColorRows(LocalSettingsSave settings)
    {
        _paletteSettings = new UI.Win98.Win98PaletteSettings { Name = "UiPaletteSettings" };
        AddChild(_paletteSettings);
        _paletteSettings.Compose(
            _settingsPanel,
            GetNode<Control>("CharacterEditorUiRoot"),
            UI.Win98.Win98Palette.Parse(settings.UiFaceColor, settings.UiBarColor, settings.UiTextColor),
            palette =>
            {
                _sandbox.Shell.EditSettings(s => s with
                {
                    UiFaceColor = palette.FaceHex,
                    UiBarColor = palette.BarHex,
                    UiTextColor = palette.TextHex,
                });
                _ = _sandbox.Shell.SavePresentationSettingsAsync();
            });
    }

    private void ComposeEffectsRows(
        LocalSettingsSave settings,
        Action<Func<LocalSettingsSave, LocalSettingsSave>> edit)
    {
        void ApplyEffects() =>
            _sandbox.ApplyEffectsSettings(
                EffectsSettings.FromSave(_sandbox.Shell.CurrentLocalSettings));

        _settingsPanel.AddToggle(
            "Reduced Motion",
            "Reduces or removes camera kicks, launches and sweeping menu transitions.",
            settings.ReducedMotion,
            value =>
            {
                edit(s => s with { ReducedMotion = value });
                ApplyEffects();
            },
            EffectsGroup);
        _settingsPanel.AddToggle(
            "Screen Shake",
            "Lets heavy hits and explosions shake the camera. Turn this off for a steady view.",
            settings.ScreenShake,
            value =>
            {
                edit(s => s with { ScreenShake = value });
                ApplyEffects();
            },
            EffectsGroup);
        _settingsPanel.AddToggle(
            "Reduced Particles",
            "Shows fewer sparks, smoke puffs and pieces of debris.",
            settings.ReducedParticles,
            value =>
            {
                edit(s => s with { ReducedParticles = value });
                ApplyEffects();
            },
            EffectsGroup);
        _settingsPanel.AddToggle(
            "Photosensitivity Safe",
            "Limits rapid flashes and strobing from fire, explosions and muzzle flashes.",
            settings.PhotosensitivitySafe,
            value =>
            {
                edit(s => s with { PhotosensitivitySafe = value });
                ApplyEffects();
            },
            EffectsGroup);
    }

    private void ComposeBehaviourRows(
        LocalSettingsSave settings,
        Action<Func<LocalSettingsSave, LocalSettingsSave>> edit)
    {
        _settingsPanel.AddChoice(
            "Start In",
            "Chooses whether the game starts in Work Mode or Play Mode.",
            StartupModeLabels,
            Math.Max(0, Array.IndexOf(StartupModes, settings.StartupInputMode)),
            index => edit(s => s with { StartupInputMode = StartupModes[index] }),
            BehaviourGroup);
        _settingsPanel.AddToggle(
            "Hide For Full-Screen Apps",
            "Hides Buddy while a game, video or presentation is full-screen.",
            settings.HideForFullscreenApps,
            value => edit(s =>
            {
                _sandbox.Lifecycle.HideForFullscreenApps = value;
                return s with { HideForFullscreenApps = value };
            }),
            BehaviourGroup);

        if (WindowsAutostart.IsSupported)
        {
            _settingsPanel.AddToggle(
                "Start With Windows",
                "Launches Desktop Buddy when you sign in to Windows.",
                settings.LaunchWithWindows && WindowsAutostart.IsEnabled(),
                value =>
                {
                    bool applied = WindowsAutostart.SetEnabled(value);
                    edit(s => s with { LaunchWithWindows = applied && value });
                },
                BehaviourGroup);
        }

        _settingsPanel.AddHotkey(
            "Work/Play Hotkey",
            "Sets the keyboard shortcut for switching between Work Mode and Play Mode.",
            string.IsNullOrWhiteSpace(settings.GlobalHotkey) ? HotkeyBinding.Default : settings.GlobalHotkey,
            chord =>
            {
                if (HotkeyBinding.Apply(InputActions.ToggleInputMode, chord))
                    edit(s => s with { GlobalHotkey = chord });
            },
            BehaviourGroup);
        _settingsPanel.AddHotkey(
            "Drop Tool Hotkey",
            "Sets the shortcut for dropping an equipped tool into the room. Double-click the tool to equip it again.",
            LocalSettingsInputBindings.DropTool(settings),
            chord =>
            {
                if (HotkeyBinding.Apply(InputActions.DropTool, chord))
                    edit(s => LocalSettingsInputBindings.WithDropTool(s, chord));
            },
            BehaviourGroup);
    }

    private void ComposeDataRows()
    {
        _settingsPanel.AddAction(
            "Save Folder",
            "Opens the folder containing your progress, settings and saved characters.",
            () => OS.ShellShowInFileManager(ProjectSettings.GlobalizePath("user://")),
            DataGroup,
            buttonText: "Open");
        _settingsPanel.AddAction(
            "Reset Progress",
            "Starts your gameplay progress over from the beginning. Your settings and saved characters are kept.",
            RequestProgressReset,
            DataGroup,
            buttonText: "Reset...");
        _settingsPanel.AddAction(
            "Show Tutorial Again",
            "Restarts the first-session tutorial. Nothing else is reset.",
            RestartTutorial,
            DataGroup,
            buttonText: "Show");
    }

    /// <summary>
    /// Clears the durable v2 record so the existing guidance controller starts over at Grab
    /// Buddy on its next frame. Owned tools, credits and characters are untouched.
    /// </summary>
    private void RestartTutorial()
    {
        Node? guidance = GetTree().Root.FindChild(
            nameof(Onboarding.FirstSessionGuidanceController), true, false);
        if (guidance is Onboarding.FirstSessionGuidanceController controller)
            controller.RestartTutorial();
    }

    /// <summary>
    /// Arms the existing two-step reset seam and asks for the second affirmative action.
    /// Cancel is the default: dismissing the prompt disarms it and changes nothing.
    /// </summary>
    private void RequestProgressReset()
    {
        _sandbox.TrayCommands.RequestResetProgress();
        if (_resetPrompt is null)
        {
            _resetPrompt = Win98Dialog.Create(
                "ResetProgressPrompt",
                "Reset Progress",
                new Vector2(360, 150),
                out VBoxContainer body,
                onClose: CancelProgressReset);
            body.AddChild(new Label
            {
                Text = "Reset all gameplay progress?\nSettings and saved characters are kept.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            });
            var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            body.AddChild(buttons);
            var confirm = new Button { Name = "ResetProgressConfirmButton", Text = "Reset" };
            confirm.Pressed += () =>
            {
                _sandbox.TrayCommands.ConfirmResetProgress();
                _resetPrompt!.Hide();
            };
            buttons.AddChild(confirm);
            var cancel = new Button { Name = "ResetProgressCancelButton", Text = "Cancel" };
            cancel.Pressed += CancelProgressReset;
            buttons.AddChild(cancel);
            GetNode<Control>("CharacterEditorUiRoot").AddChild(_resetPrompt);
        }

        _resetPrompt.Show();
    }

    private void CancelProgressReset()
    {
        _sandbox.TrayCommands.CancelResetProgress();
        _resetPrompt?.Hide();
    }
}

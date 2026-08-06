using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Keyboard access for the integrated paint workspace. Commands are routed through the existing
/// UI buttons so pointer, menu and keyboard activation continue to share one behavior path.
/// </summary>
public partial class Win98PaintShortcutBootstrap : Node
{
    private const string RotateLeftCommand = "@rotate-left";
    private const string RotateRightCommand = "@rotate-right";

    private PaintCanvasControl? _canvas;
    private bool _tooltipsDecorated;

    public override void _Ready()
    {
        // CharacterEditorModeCoordinator pauses gameplay while the editor is open.
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_canvas))
        {
            _canvas = GetTree().Root.FindChild(
                "CharacterPaintCanvas", recursive: true, owned: false) as PaintCanvasControl;
            _tooltipsDecorated = false;
        }

        if (GodotObject.IsInstanceValid(_canvas) && !_tooltipsDecorated)
            DecorateTooltips();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true } key ||
            (key.Echo && key.Keycode is not (Key.Bracketleft or Key.Bracketright)) ||
            !GodotObject.IsInstanceValid(_canvas) ||
            !_canvas!.IsVisibleInTree() ||
            IsTextEntryFocused())
        {
            return;
        }

        string? command = ResolveCommand(key);
        if (command is null)
            return;

        Button? button = ResolveButton(command);
        if (!GodotObject.IsInstanceValid(button) || button!.Disabled || !button.IsVisibleInTree())
            return;

        // Hold-repeat buttons (brush size) act on button down/up, not on Pressed.
        if (button.HasNode("Repeat"))
        {
            button.EmitSignal(BaseButton.SignalName.ButtonDown);
            button.EmitSignal(BaseButton.SignalName.ButtonUp);
        }
        else
        {
            button.EmitSignal(Button.SignalName.Pressed);
        }

        GetViewport().SetInputAsHandled();
    }

    private static string? ResolveCommand(InputEventKey key)
    {
        if (key.AltPressed || key.MetaPressed)
            return null;

        if (key.CtrlPressed)
        {
            return key.Keycode switch
            {
                Key.S when !key.ShiftPressed => "SaveCharacterButton",
                Key.W when !key.ShiftPressed => "CloseCharacterEditorButton",
                Key.Enter when !key.ShiftPressed => "UseCharacterButton",
                _ => null,
            };
        }

        return key.Keycode switch
        {
            Key.B => "PaintBrushButton",
            Key.E => "PaintEraserButton",
            Key.I => "PaintEyedropperButton",
            Key.H => "PaintPanButton",
            Key.Bracketleft => "PaintSizeDecreaseButton",
            Key.Bracketright => "PaintSizeIncreaseButton",
            Key.Minus => "PaintZoomOutButton",
            Key.Equal => "PaintZoomInButton",
            Key.Home => "PaintResetViewButton",
            Key.R => RotateLeftCommand,
            Key.T => RotateRightCommand,
            _ => null,
        };
    }

    private Button? ResolveButton(string command)
    {
        if (command is RotateLeftCommand or RotateRightCommand)
        {
            if (GetTree().Root.FindChild(
                    "PaintRotateRow", recursive: true, owned: false) is not HBoxContainer row)
            {
                return null;
            }

            int index = command == RotateLeftCommand ? 0 : 1;
            return index < row.GetChildCount() ? row.GetChild(index) as Button : null;
        }

        return GetTree().Root.FindChild(command, recursive: true, owned: false) as Button;
    }

    private bool IsTextEntryFocused()
    {
        Control? focus = GetViewport().GuiGetFocusOwner();
        return focus is LineEdit or TextEdit or SpinBox;
    }

    private void DecorateTooltips()
    {
        AddShortcut("PaintBrushButton", "B");
        AddShortcut("PaintEraserButton", "E");
        AddShortcut("PaintEyedropperButton", "I");
        AddShortcut("PaintPanButton", "H");
        AddShortcut("PaintSizeDecreaseButton", "[");
        AddShortcut("PaintSizeIncreaseButton", "]");
        AddShortcut("PaintZoomOutButton", "-");
        AddShortcut("PaintZoomInButton", "+");
        AddShortcut("PaintResetViewButton", "Home");
        AddShortcut(ResolveButton(RotateLeftCommand), "R");
        AddShortcut(ResolveButton(RotateRightCommand), "T");
        AddShortcut("SaveCharacterButton", "Ctrl+S");
        AddShortcut("UseCharacterButton", "Ctrl+Enter");
        AddShortcut("CloseCharacterEditorButton", "Ctrl+W");
        _tooltipsDecorated = true;
    }

    private void AddShortcut(string buttonName, string shortcut) => AddShortcut(
        GetTree().Root.FindChild(buttonName, recursive: true, owned: false) as Button,
        shortcut);

    private static void AddShortcut(Button? button, string shortcut)
    {
        if (!GodotObject.IsInstanceValid(button) || button!.TooltipText.Contains($"({shortcut})"))
            return;
        button.TooltipText = string.IsNullOrWhiteSpace(button.TooltipText)
            ? shortcut
            : $"{button.TooltipText} ({shortcut})";
    }
}

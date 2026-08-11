using System;
using System.Linq;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Stable Win98 paint-editor UX corrections that depend on the editor's deferred composition.
/// Every mutation is idempotent so late-created controls are corrected without one-frame races.
/// </summary>
public partial class Win98PaintUxPolishBootstrap : Node
{
    private static readonly string[] CatchPhrases =
    [
        "mighty warrior", "next-door neighbour", "mortal enemy", "best friend",
        "creative spark", "tiny troublemaker", "future legend", "loyal sidekick",
        "lovable menace", "brave explorer", "chaotic roommate", "pocket-sized hero",
        "secret mastermind", "gentle giant", "fearless captain", "curious inventor",
        "dramatic rival", "trusted companion", "neighbourhood celebrity", "midnight gremlin",
        "cheerful guardian", "unlikely champion", "clever prankster", "cozy confidant",
        "bold adventurer", "mischievous genius", "steadfast ally", "adorable disaster",
        "daring outlaw", "friendly rival", "eccentric roommate", "legendary nuisance",
        "tiny tactician", "lifelong companion",
    ];

    private readonly RandomNumberGenerator _random = new();
    private CharacterEditorHost? _host;
    private ItemList? _library;
    private Button? _newButton;
    private Control? _modalBlocker;
    private PanelContainer? _newCharacterPanel;
    private LineEdit? _newCharacterName;
    private Label? _motivation;
    private PanelContainer? _deletePanel;
    private Control? _deleteBlocker;
    private Label? _deleteMessage;
    private bool _openNewPromptAfterUnsaved;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _random.Randomize();
    }

    public override void _Process(double delta)
    {
        ResolveHost();
        if (!GodotObject.IsInstanceValid(_host) || !_host!.IsEditorOpen)
            return;

        _library ??= GetTree().Root.FindChild("CharacterLibraryList", true, false) as ItemList;
        if (GetTree().Root.FindChild("CharacterEditorUiRoot", true, false) is Control root)
        {
            EnsureNewCharacterPrompt(root);
            EnsureDeletePrompt(root);
        }

        CorrectCharacterColumn();
        CorrectToolRail();
        CorrectUnsavedPrompt();
        ApplyColorPickerIcon();
        CleanLibraryTooltips();
        ResolvePendingNewPrompt();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key ||
            !GodotObject.IsInstanceValid(_newCharacterPanel) || !_newCharacterPanel!.Visible)
            return;

        if (key.Keycode == Key.Escape)
        {
            CloseNewCharacterPrompt();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ResolveHost()
    {
        if (!GodotObject.IsInstanceValid(_host))
            _host = GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
    }

    private void CorrectCharacterColumn()
    {
        if (GetTree().Root.FindChild("Win98CharacterColumnBody", true, false) is not VBoxContainer column ||
            !GodotObject.IsInstanceValid(_host?.NewButton) ||
            !GodotObject.IsInstanceValid(_library))
            return;

        HideLocalCharactersLabel();

        Button original = _host!.NewButton;
        original.Visible = false;
        original.FocusMode = Control.FocusModeEnum.None;

        if (!GodotObject.IsInstanceValid(_newButton))
        {
            _newButton = column.FindChild("Win98NewCharacterButton", false, false) as Button;
            if (!GodotObject.IsInstanceValid(_newButton))
            {
                _newButton = new Button
                {
                    Name = "Win98NewCharacterButton",
                    Text = "+ New Character",
                    TooltipText = "Create and name a new buddy.",
                    FocusMode = Control.FocusModeEnum.All,
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                    CustomMinimumSize = new Vector2(0, 38),
                };
                _newButton.Pressed += RequestNewCharacterPrompt;
                column.AddChild(_newButton);
            }
        }

        if (_newButton!.GetParent() != column)
            _newButton.Reparent(column, false);
        int desiredIndex = Math.Min(_library!.GetParent() == column ? _library.GetIndex() + 1 : 2, column.GetChildCount() - 1);
        if (_newButton.GetIndex() != desiredIndex)
            column.MoveChild(_newButton, desiredIndex);

        // The pager lives inside the reparented CharacterLibrary column, i.e. a grandchild of
        // this column — search recursively or it stays visible.
        Button? previous = column.FindChild("PreviousButton", true, false) as Button;
        Button? next = column.FindChild("NextButton", true, false) as Button;
        Control? pager = previous?.GetParent() as Control ?? next?.GetParent() as Control;
        if (GodotObject.IsInstanceValid(pager))
        {
            pager!.Visible = false;
            pager.FocusMode = Control.FocusModeEnum.None;
            foreach (Node item in pager.GetChildren())
                if (item is Control control) control.FocusMode = Control.FocusModeEnum.None;
        }

        Button duplicate = _host.DuplicateButton;
        // The host's Delete acts immediately; the product flow asks first, so it is replaced
        // by a confirming button of our own.
        Button delete = _host.DeleteButton;
        delete.Visible = false;
        delete.FocusMode = Control.FocusModeEnum.None;
        _host.RandomizeButton.Visible = false;
        _host.RandomizeButton.FocusMode = Control.FocusModeEnum.None;

        if (duplicate.GetParent() is HBoxContainer manage)
        {
            Button confirmDelete = EnsureConfirmingDeleteButton(manage);
            manage.AddThemeConstantOverride("separation", 2);
            confirmDelete.Disabled = delete.Disabled;
            duplicate.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            duplicate.CustomMinimumSize = new Vector2(0, 30);
            foreach (Node child in manage.GetChildren())
            {
                if (child != duplicate && child != confirmDelete && child is Control control)
                    control.Visible = false;
            }
        }
    }

    private void HideLocalCharactersLabel()
    {
        if (_library?.GetParent() is not Control parent)
            return;
        foreach (Node child in parent.GetChildren())
            if (child is Label { Text: "Local Characters" } label) label.Visible = false;
    }

    private void CorrectToolRail()
    {
        if (GetTree().Root.FindChild("Win98ToolPicker", true, false) is not GridContainer picker)
            return;

        Button? brush = picker.FindChild("PaintBrushButton", true, false) as Button;
        Button? pen = picker.FindChild("PaintPenButton", true, false) as Button;
        Button? eraser = picker.FindChild("PaintEraserButton", true, false) as Button;
        Button? pick = picker.FindChild("PaintEyedropperButton", true, false) as Button;
        Button? pan = picker.FindChild("PaintPanButton", true, false) as Button;
        if (!GodotObject.IsInstanceValid(brush) || !GodotObject.IsInstanceValid(pen) || !GodotObject.IsInstanceValid(eraser) ||
            !GodotObject.IsInstanceValid(pick) || !GodotObject.IsInstanceValid(pan))
            return;

        picker.Columns = 1;
        picker.CustomMinimumSize = new Vector2(108, 0);
        picker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        Label paintHeader = EnsureHeader(picker, "PaintToolGroupHeader", "Paint");
        HSeparator separator = picker.FindChild("PaintToolGroupSeparator", false, false) as HSeparator
            ?? new HSeparator { Name = "PaintToolGroupSeparator" };
        if (separator.GetParent() is null) picker.AddChild(separator);
        Label inspectHeader = EnsureHeader(picker, "PaintInspectGroupHeader", "Inspect & move");

        Move(picker, paintHeader, 0);
        Move(picker, brush!, 1);
        Move(picker, pen!, 2);
        Move(picker, eraser!, 3);
        Move(picker, separator, 4);
        Move(picker, inspectHeader, 5);
        Move(picker, pick!, 6);
        Move(picker, pan!, 7);

        ConfigureToolButton(brush!, "Brush  [B]", "Paint with the selected color. (B)");
        ConfigureToolButton(pen!, "Pen  [P]", "Paint with a solid pen nib. (P)");
        ConfigureToolButton(eraser!, "Eraser  [E]", "Remove paint from the buddy. (E)");
        ConfigureToolButton(pick!, "Pick Color  [I]", "Sample a painted color from the buddy. (I)");
        ConfigureToolButton(pan!, "Pan View  [H]", "Move the canvas without painting. (H)");
    }

    private static Label EnsureHeader(GridContainer picker, string name, string text)
    {
        if (picker.FindChild(name, false, false) is Label existing)
            return existing;
        var label = new Label { Name = name, Text = text, HorizontalAlignment = HorizontalAlignment.Left };
        label.AddThemeColorOverride("font_color", Win98ThemeFactory.Shadow);
        picker.AddChild(label);
        return label;
    }

    private static void ConfigureToolButton(Button button, string text, string tooltip)
    {
        button.Text = text;
        button.TooltipText = tooltip;
        button.Alignment = HorizontalAlignment.Left;
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.CustomMinimumSize = new Vector2(108, 31);
    }

    private static void Move(Node parent, Node child, int index)
    {
        if (child.GetParent() != parent)
            child.Reparent(parent, false);
        int safe = Math.Clamp(index, 0, parent.GetChildCount() - 1);
        if (child.GetIndex() != safe)
            parent.MoveChild(child, safe);
    }

    private void CorrectUnsavedPrompt()
    {
        if (GetTree().Root.FindChild("UnsavedChangesPrompt", true, false) is not PanelContainer panel ||
            panel.GetChildCount() == 0)
            return;

        panel.CustomMinimumSize = new Vector2(410, 210);
        panel.OffsetLeft = -205;
        panel.OffsetTop = -105;
        panel.OffsetRight = 205;
        panel.OffsetBottom = 105;
        panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 3));
        panel.Theme ??= Win98ThemeFactory.Create();

        VBoxContainer? box = panel.FindChild("UnsavedPromptContent", true, false) as VBoxContainer;
        if (box is null)
        {
            if (panel.GetChild(0) is not VBoxContainer legacy)
                return;
            legacy.Name = "UnsavedPromptContent";
            var margin = new MarginContainer { Name = "UnsavedPromptMargin" };
            panel.AddChild(margin);
            legacy.Reparent(margin, false);
            box = legacy;
        }
        // The title bar is the first child of the content box, so the surrounding margin must
        // stay flush with the panel border or the blue bar floats like the screenshot shows.
        if (box.GetParent() is MarginContainer promptMargin)
        {
            promptMargin.AddThemeConstantOverride("margin_left", 0);
            promptMargin.AddThemeConstantOverride("margin_top", 0);
            promptMargin.AddThemeConstantOverride("margin_right", 0);
            promptMargin.AddThemeConstantOverride("margin_bottom", 12);
        }
        box.AddThemeConstantOverride("separation", 10);

        PanelContainer titleBar = box.FindChild("UnsavedPromptTitleBar", false, false) as PanelContainer
            ?? new PanelContainer { Name = "UnsavedPromptTitleBar" };
        if (titleBar.GetParent() is null)
        {
            titleBar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
            titleBar.AddChild(new Label { Name = "UnsavedPromptTitle", Text = "Are you sure?" });
            box.AddChild(titleBar);
        }
        if (titleBar.FindChild("UnsavedPromptTitle", true, false) is Label title)
        {
            title.Text = "Are you sure?";
            title.AddThemeColorOverride("font_color", Win98ThemeFactory.Light);
        }
        Move(box, titleBar, 0);

        HBoxContainer? actions = box.GetChildren().OfType<HBoxContainer>()
            .FirstOrDefault(row => row.FindChildren("*", nameof(Button), true, false).Count > 0);
        if (actions is null)
            return;
        actions.Alignment = BoxContainer.AlignmentMode.Center;
        actions.AddThemeConstantOverride("separation", 8);
        actions.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        foreach (Node node in actions.GetChildren())
            if (node is Button button) button.CustomMinimumSize = new Vector2(96, 30);

        Control spacer = box.FindChild("UnsavedPromptSpacer", false, false) as Control
            ?? new Control { Name = "UnsavedPromptSpacer", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        if (spacer.GetParent() is null) box.AddChild(spacer);
        Move(box, spacer, Math.Max(1, actions.GetIndex() - 1));
        Move(box, actions, box.GetChildCount() - 1);
    }

    private Button EnsureConfirmingDeleteButton(HBoxContainer manage)
    {
        if (manage.FindChild("Win98DeleteCharacterButton", false, false) is Button existing)
            return existing;

        var button = new Button
        {
            Name = "Win98DeleteCharacterButton",
            Text = "Delete",
            TooltipText = "Delete the selected character.",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 30),
            FocusMode = Control.FocusModeEnum.All,
        };
        button.Pressed += OpenDeletePrompt;
        manage.AddChild(button);
        return button;
    }

    private void EnsureDeletePrompt(Control root)
    {
        if (GodotObject.IsInstanceValid(_deletePanel))
            return;

        _deleteBlocker = Win98Dialog.Blocker(root, "Win98DeleteCharacterModalBlocker");
        if (root.FindChild("Win98DeleteCharacterPrompt", false, false) is PanelContainer existing)
        {
            _deletePanel = existing;
            _deleteMessage = existing.FindChild("DeleteCharacterMessage", true, false) as Label;
            return;
        }

        _deletePanel = Win98Dialog.Create(
            "Win98DeleteCharacterPrompt",
            "Are you sure?",
            new Vector2(410, 200),
            out VBoxContainer body,
            CloseDeletePrompt);
        root.AddChild(_deletePanel);

        _deleteMessage = new Label
        {
            Name = "DeleteCharacterMessage",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        body.AddChild(_deleteMessage);
        body.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        var actions = new HBoxContainer
        {
            Name = "DeleteCharacterActions",
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
        };
        actions.AddThemeConstantOverride("separation", 8);
        body.AddChild(actions);
        Win98Dialog.Action(actions, "Cancel", CloseDeletePrompt);
        Win98Dialog.Action(actions, "Delete", ConfirmDelete);
    }

    private void OpenDeletePrompt()
    {
        if (!GodotObject.IsInstanceValid(_deletePanel) || !GodotObject.IsInstanceValid(_host))
            return;
        string name = _host!.Session.WorkingDocument?.DisplayName ?? "this character";
        _deleteMessage!.Text = $"You are about to delete {name}. Are you sure?";
        _deleteBlocker!.Visible = true;
        _deletePanel!.Visible = true;
        _deletePanel.MoveToFront();
    }

    private void CloseDeletePrompt()
    {
        if (GodotObject.IsInstanceValid(_deletePanel)) _deletePanel!.Visible = false;
        if (GodotObject.IsInstanceValid(_deleteBlocker)) _deleteBlocker!.Visible = false;
    }

    private async void ConfirmDelete()
    {
        CloseDeletePrompt();
        if (GodotObject.IsInstanceValid(_host))
            await _host!.Session.DeleteAsync();
    }

    private void EnsureNewCharacterPrompt(Control root)
    {
        if (GodotObject.IsInstanceValid(_newCharacterPanel))
            return;

        _modalBlocker = Win98Dialog.Blocker(root, "Win98NewCharacterModalBlocker");

        _newCharacterPanel = root.FindChild("Win98NewCharacterPrompt", false, false) as PanelContainer;
        if (GodotObject.IsInstanceValid(_newCharacterPanel))
        {
            _newCharacterName = _newCharacterPanel!.FindChild("NewCharacterNameInput", true, false) as LineEdit;
            _motivation = _newCharacterPanel.FindChild("NewCharacterMotivation", true, false) as Label;
            return;
        }

        _newCharacterPanel = Win98Dialog.Create(
            "Win98NewCharacterPrompt",
            "Create New Character",
            new Vector2(430, 230),
            out VBoxContainer column,
            draggable: false);
        root.AddChild(_newCharacterPanel);

        _motivation = new Label
        {
            Name = "NewCharacterMotivation",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        column.AddChild(_motivation);
        column.AddChild(new Label { Text = "Name:" });

        _newCharacterName = new LineEdit
        {
            Name = "NewCharacterNameInput",
            PlaceholderText = "Enter a buddy name",
            MaxLength = 48,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _newCharacterName.TextSubmitted += _ => CreateNamedCharacter();
        column.AddChild(_newCharacterName);

        column.AddChild(new Control
        {
            Name = "NewCharacterSpacer",
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        });

        var actions = new HBoxContainer
        {
            Name = "NewCharacterActions",
            Alignment = BoxContainer.AlignmentMode.End,
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
        };
        actions.AddThemeConstantOverride("separation", 8);
        column.AddChild(actions);
        var cancel = new Button { Text = "Cancel", CustomMinimumSize = new Vector2(96, 30) };
        cancel.Pressed += CloseNewCharacterPrompt;
        actions.AddChild(cancel);
        var create = new Button { Text = "Create", CustomMinimumSize = new Vector2(96, 30) };
        create.Pressed += CreateNamedCharacter;
        actions.AddChild(create);
    }

    private void OpenNewCharacterPrompt()
    {
        if (!GodotObject.IsInstanceValid(_newCharacterPanel) ||
            !GodotObject.IsInstanceValid(_newCharacterName) ||
            !GodotObject.IsInstanceValid(_motivation))
            return;

        _motivation!.Text = $"Create your {CatchPhrases[_random.RandiRange(0, CatchPhrases.Length - 1)]}!";
        _newCharacterName!.Text = string.Empty;
        _newCharacterName.PlaceholderText = "Enter a buddy name";
        _modalBlocker!.Visible = true;
        _newCharacterPanel!.Visible = true;
        _newCharacterPanel.MoveToFront();
        _newCharacterName.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void RequestNewCharacterPrompt()
    {
        if (!GodotObject.IsInstanceValid(_host))
            return;
        CharacterEditorActionResult result = _host!.RequestNewCharacterPrompt();
        if (result.NeedsUnsavedDecision)
        {
            _openNewPromptAfterUnsaved = true;
            return;
        }
        if (result.Completed)
            OpenNewCharacterPrompt();
    }

    private void CloseNewCharacterPrompt()
    {
        if (GodotObject.IsInstanceValid(_newCharacterPanel)) _newCharacterPanel!.Visible = false;
        if (GodotObject.IsInstanceValid(_modalBlocker)) _modalBlocker!.Visible = false;
        _newButton?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CreateNamedCharacter()
    {
        if (!GodotObject.IsInstanceValid(_newCharacterName) || !GodotObject.IsInstanceValid(_host))
            return;
        string name = _newCharacterName!.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            _newCharacterName.PlaceholderText = "A name is required";
            _newCharacterName.GrabFocus();
            return;
        }

        _host.Session.NewCharacter(name);
        CloseNewCharacterPrompt();
    }

    private void ResolvePendingNewPrompt()
    {
        if (!_openNewPromptAfterUnsaved ||
            _host!.Session.PendingAction != CharacterEditorPendingAction.None)
            return;
        _openNewPromptAfterUnsaved = false;
        if (!_host.Session.IsDirty)
            OpenNewCharacterPrompt();
    }

    private void CleanLibraryTooltips()
    {
        if (!GodotObject.IsInstanceValid(_library))
            return;
        for (int index = 0; index < _library!.ItemCount; index++)
            _library.SetItemTooltip(index, string.Empty);
    }

    private void ApplyColorPickerIcon()
    {
        if (GetTree().Root.FindChild("PaintColorWheel", true, false) is not ColorPickerButton picker)
            return;
        // ColorPickerButton paints its color swatch over the whole button *after* the button
        // draws, so Icon/Text are invisible. Overlay the glyph as a child instead.
        picker.Icon = null;
        if (picker.FindChild("PaintColorWheelGreyFace", false, false) is not PanelContainer face)
        {
            face = new PanelContainer
            {
                Name = "PaintColorWheelGreyFace",
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            face.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 2));
            picker.AddChild(face);
            face.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        }
        picker.MoveChild(face, 0);
        if (picker.FindChild("PaintColorWheelIcon", false, false) is null)
        {
            var overlay = new TextureRect
            {
                Name = "PaintColorWheelIcon",
                Texture = GD.Load<Texture2D>("res://assets/ui/win98/paint_bucket_brushes.svg"),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            picker.AddChild(overlay);
            overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            overlay.OffsetLeft = 5;
            overlay.OffsetTop = 5;
            overlay.OffsetRight = -5;
            overlay.OffsetBottom = -5;
        }
        if (picker.FindChild("PaintColorWheelIcon", false, false) is Node icon)
            picker.MoveChild(icon, picker.GetChildCount() - 1);
        picker.Text = string.Empty;
        picker.TooltipText = "Open the full color picker.";
        picker.ExpandIcon = false;
    }
}

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
    private string? _pendingNewName;
    private Guid? _pendingPreviousCharacter;

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
            EnsureNewCharacterPrompt(root);

        CorrectCharacterColumn();
        CorrectToolRail();
        CorrectUnsavedPrompt();
        ApplyColorPickerIcon();
        CleanLibraryTooltips();
        ResolvePendingNamedCharacter();
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
                _newButton.Pressed += OpenNewCharacterPrompt;
                column.AddChild(_newButton);
            }
        }

        if (_newButton!.GetParent() != column)
            _newButton.Reparent(column, false);
        int desiredIndex = Math.Min(_library!.GetParent() == column ? _library.GetIndex() + 1 : 2, column.GetChildCount() - 1);
        if (_newButton.GetIndex() != desiredIndex)
            column.MoveChild(_newButton, desiredIndex);

        foreach (Node child in column.GetChildren())
        {
            if (child is HBoxContainer row &&
                (row.FindChild("PreviousButton", true, false) is Button ||
                 row.FindChild("NextButton", true, false) is Button))
            {
                row.Visible = false;
                foreach (Node item in row.GetChildren())
                    if (item is Control control) control.FocusMode = Control.FocusModeEnum.None;
            }
        }

        Button duplicate = _host.DuplicateButton;
        Button delete = _host.DeleteButton;
        _host.RandomizeButton.Visible = false;
        _host.RandomizeButton.FocusMode = Control.FocusModeEnum.None;

        if (duplicate.GetParent() is HBoxContainer manage)
        {
            duplicate.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            delete.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            duplicate.CustomMinimumSize = new Vector2(0, 30);
            delete.CustomMinimumSize = new Vector2(0, 30);
            if (delete.GetParent() != manage)
                delete.Reparent(manage, false);
            foreach (Node child in manage.GetChildren())
            {
                if (child != duplicate && child != delete && child is Control control)
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
        Button? eraser = picker.FindChild("PaintEraserButton", true, false) as Button;
        Button? pick = picker.FindChild("PaintEyedropperButton", true, false) as Button;
        Button? pan = picker.FindChild("PaintPanButton", true, false) as Button;
        if (!GodotObject.IsInstanceValid(brush) || !GodotObject.IsInstanceValid(eraser) ||
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
        Move(picker, eraser!, 2);
        Move(picker, separator, 3);
        Move(picker, inspectHeader, 4);
        Move(picker, pick!, 5);
        Move(picker, pan!, 6);

        ConfigureToolButton(brush!, "Brush  [B]", "Paint with the selected color. (B)");
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
            margin.AddThemeConstantOverride("margin_left", 14);
            margin.AddThemeConstantOverride("margin_top", 10);
            margin.AddThemeConstantOverride("margin_right", 14);
            margin.AddThemeConstantOverride("margin_bottom", 14);
            panel.AddChild(margin);
            legacy.Reparent(margin, false);
            box = legacy;
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

    private void EnsureNewCharacterPrompt(Control root)
    {
        if (GodotObject.IsInstanceValid(_newCharacterPanel))
            return;

        _modalBlocker = root.FindChild("Win98NewCharacterModalBlocker", false, false) as Control;
        if (!GodotObject.IsInstanceValid(_modalBlocker))
        {
            _modalBlocker = new ColorRect
            {
                Name = "Win98NewCharacterModalBlocker",
                Color = new Color(0, 0, 0, 0.35f),
                Visible = false,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            _modalBlocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(_modalBlocker);
        }

        _newCharacterPanel = root.FindChild("Win98NewCharacterPrompt", false, false) as PanelContainer;
        if (GodotObject.IsInstanceValid(_newCharacterPanel))
        {
            _newCharacterName = _newCharacterPanel!.FindChild("NewCharacterNameInput", true, false) as LineEdit;
            _motivation = _newCharacterPanel.FindChild("NewCharacterMotivation", true, false) as Label;
            return;
        }

        _newCharacterPanel = new PanelContainer
        {
            Name = "Win98NewCharacterPrompt",
            Visible = false,
            ProcessMode = ProcessModeEnum.Always,
            CustomMinimumSize = new Vector2(430, 230),
            Theme = Win98ThemeFactory.Create(),
        };
        _newCharacterPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _newCharacterPanel.OffsetLeft = -215;
        _newCharacterPanel.OffsetTop = -115;
        _newCharacterPanel.OffsetRight = 215;
        _newCharacterPanel.OffsetBottom = 115;
        _newCharacterPanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 3));
        root.AddChild(_newCharacterPanel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 14);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        _newCharacterPanel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        margin.AddChild(column);

        var titleBar = new PanelContainer();
        titleBar.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        column.AddChild(titleBar);
        var title = new Label { Text = "Create New Character" };
        title.AddThemeColorOverride("font_color", Win98ThemeFactory.Light);
        titleBar.AddChild(title);

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

        Guid? before = _host!.Session.SelectedCharacterId;
        CharacterEditorActionResult result = _host.Session.NewCharacter(name);
        CloseNewCharacterPrompt();
        if (result.NeedsUnsavedDecision)
        {
            _pendingNewName = name;
            _pendingPreviousCharacter = before;
        }
    }

    private void ResolvePendingNamedCharacter()
    {
        if (string.IsNullOrWhiteSpace(_pendingNewName) ||
            _host!.Session.PendingAction != CharacterEditorPendingAction.None)
            return;

        CharacterDocument? document = _host.Session.WorkingDocument;
        if (document is not null && document.Id != _pendingPreviousCharacter &&
            string.Equals(document.DisplayName, "New Character", StringComparison.Ordinal))
            _host.Session.Rename(_pendingNewName);
        _pendingNewName = null;
        _pendingPreviousCharacter = null;
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
        if (picker.Icon is null)
            picker.Icon = GD.Load<Texture2D>("res://assets/ui/win98/paint_bucket_brushes.svg");
        picker.Text = string.Empty;
        picker.TooltipText = "Open the full color picker.";
        picker.ExpandIcon = false;
    }
}

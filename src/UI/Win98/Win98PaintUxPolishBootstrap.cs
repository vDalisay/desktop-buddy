using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Final UX polish for the Win98 paint workspace: a named new-character flow, cleaner library,
/// contextual layer help, creative-app tool grouping, and Win98-consistent modal styling.
/// </summary>
public partial class Win98PaintUxPolishBootstrap : Node
{
    private const string LayerHelp =
        "Hidden layers cannot receive paint and return when the editor closes.";

    private static readonly string[] CatchPhrases =
    [
        "mighty warrior",
        "next-door neighbour",
        "mortal enemy",
        "best friend",
        "creative spark",
        "tiny troublemaker",
        "future legend",
        "loyal sidekick",
        "lovable menace",
        "brave explorer",
        "chaotic roommate",
        "pocket-sized hero",
        "secret mastermind",
        "gentle giant",
        "fearless captain",
        "curious inventor",
        "dramatic rival",
        "trusted companion",
        "neighbourhood celebrity",
        "midnight gremlin",
        "cheerful guardian",
        "unlikely champion",
        "clever prankster",
        "cozy confidant",
        "bold adventurer",
        "mischievous genius",
        "steadfast ally",
        "adorable disaster",
        "daring outlaw",
        "friendly rival",
        "eccentric roommate",
        "legendary nuisance",
        "tiny tactician",
        "lifelong companion",
    ];

    private readonly RandomNumberGenerator _random = new();
    private CharacterEditorHost? _host;
    private ItemList? _library;
    private Button? _replacementNewButton;
    private Control? _modalBlocker;
    private PanelContainer? _newCharacterPanel;
    private LineEdit? _newCharacterName;
    private Label? _motivation;
    private string? _pendingNewName;
    private Guid? _pendingPreviousCharacter;
    private bool _composed;
    private bool _libraryHadTransient;

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

        if (!_composed)
            TryCompose();

        if (!_composed)
            return;

        PolishLibrary();
        ResolvePendingNamedCharacter();
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (input is not InputEventKey { Pressed: true, Echo: false } key ||
            !GodotObject.IsInstanceValid(_newCharacterPanel) ||
            !_newCharacterPanel!.Visible)
        {
            return;
        }

        if (key.Keycode == Key.Escape)
        {
            CloseNewCharacterPrompt();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ResolveHost()
    {
        if (!GodotObject.IsInstanceValid(_host))
        {
            _host = GetTree().Root.FindChild(
                nameof(CharacterEditorHost), recursive: true, owned: false) as CharacterEditorHost;
            _composed = false;
        }
    }

    private void TryCompose()
    {
        if (!GodotObject.IsInstanceValid(_host?.NewButton) ||
            GetTree().Root.FindChild("CharacterEditorUiRoot", true, false) is not Control uiRoot)
        {
            return;
        }

        _library = GetTree().Root.FindChild(
            "CharacterLibraryList", recursive: true, owned: false) as ItemList;
        ReplaceNewCharacterButton();
        RemoveLibraryChrome();
        MoveRandomizeOutOfPaint();
        AddLayerHelpTooltip();
        PolishToolPicker();
        StyleUnsavedPrompt();
        BuildNewCharacterPrompt(uiRoot);
        _composed = GodotObject.IsInstanceValid(_replacementNewButton) &&
            GodotObject.IsInstanceValid(_newCharacterPanel);
    }

    private void ReplaceNewCharacterButton()
    {
        Button original = _host!.NewButton;
        if (!GodotObject.IsInstanceValid(original) || original.GetParent() is not Control parent)
            return;

        if (parent.FindChild("Win98NewCharacterButton", false, false) is Button existing)
        {
            _replacementNewButton = existing;
            original.Visible = false;
            return;
        }

        _replacementNewButton = new Button
        {
            Name = "Win98NewCharacterButton",
            Text = "+ New Character",
            TooltipText = "Create and name a new buddy.",
            FocusMode = Control.FocusModeEnum.All,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 38),
        };
        _replacementNewButton.Pressed += OpenNewCharacterPrompt;
        int index = original.GetIndex();
        parent.AddChild(_replacementNewButton);
        parent.MoveChild(_replacementNewButton, index);
        original.Visible = false;
    }

    private void RemoveLibraryChrome()
    {
        if (!GodotObject.IsInstanceValid(_library))
            return;

        if (_library!.GetParent() is Control parent)
        {
            foreach (Node child in parent.GetChildren())
            {
                if (child is Label { Text: "Local Characters" } label)
                    label.Visible = false;
            }
        }
    }

    private void MoveRandomizeOutOfPaint()
    {
        if (GodotObject.IsInstanceValid(_host?.RandomizeButton))
        {
            _host!.RandomizeButton.Visible = false;
            _host.RandomizeButton.TooltipText =
                "Randomize belongs to the upcoming customization and clothing workspace.";
        }
    }

    private void AddLayerHelpTooltip()
    {
        if (GetTree().Root.FindChild("Win98PaintLayerPanel", true, false) is not PanelContainer panel ||
            panel.GetChildCount() == 0 || panel.GetChild(0) is not VBoxContainer column)
        {
            return;
        }

        if (column.FindChild("PaintLayerHelpButton", true, false) is Button)
            return;

        Label? header = column.GetChildren().OfType<Label>()
            .FirstOrDefault(label => label.Text == "Layers");
        if (header is null)
            return;

        var headerRow = new HBoxContainer
        {
            Name = "PaintLayerHeaderRow",
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        headerRow.AddThemeConstantOverride("separation", 3);
        int index = header.GetIndex();
        column.AddChild(headerRow);
        column.MoveChild(headerRow, index);
        header.Reparent(headerRow, false);
        header.HorizontalAlignment = HorizontalAlignment.Center;

        var help = new Button
        {
            Name = "PaintLayerHelpButton",
            Text = "?",
            TooltipText = LayerHelp,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(22, 22),
        };
        headerRow.AddChild(help);

        foreach (Label label in column.GetChildren().OfType<Label>())
        {
            if (label.Text == LayerHelp)
                label.Visible = false;
        }
    }

    private void PolishToolPicker()
    {
        if (GetTree().Root.FindChild("Win98ToolPicker", true, false) is not GridContainer picker ||
            picker.FindChild("PaintToolGroupHeader", false, false) is Label)
        {
            return;
        }

        Button? brush = picker.FindChild("PaintBrushButton", true, false) as Button;
        Button? eraser = picker.FindChild("PaintEraserButton", true, false) as Button;
        Button? eyedropper = picker.FindChild("PaintEyedropperButton", true, false) as Button;
        Button? pan = picker.FindChild("PaintPanButton", true, false) as Button;
        if (!GodotObject.IsInstanceValid(brush) || !GodotObject.IsInstanceValid(eraser) ||
            !GodotObject.IsInstanceValid(eyedropper) || !GodotObject.IsInstanceValid(pan))
        {
            return;
        }

        picker.Columns = 1;
        picker.CustomMinimumSize = new Vector2(108, 0);

        var paintHeader = ToolHeader("PaintToolGroupHeader", "Paint");
        picker.AddChild(paintHeader);
        picker.MoveChild(paintHeader, 0);

        var separator = new HSeparator { Name = "PaintToolGroupSeparator" };
        picker.AddChild(separator);
        picker.MoveChild(separator, eraser!.GetIndex() + 1);

        var inspectHeader = ToolHeader("PaintInspectGroupHeader", "Inspect & move");
        picker.AddChild(inspectHeader);
        picker.MoveChild(inspectHeader, separator.GetIndex() + 1);

        ConfigureToolButton(brush!, "Brush", "B", "Paint with the selected color.");
        ConfigureToolButton(eraser!, "Eraser", "E", "Remove paint from the buddy.");
        ConfigureToolButton(eyedropper!, "Pick Color", "I", "Sample a painted color from the buddy.");
        ConfigureToolButton(pan!, "Pan View", "H", "Move the canvas without painting.");
    }

    private static Label ToolHeader(string name, string text)
    {
        var label = new Label
        {
            Name = name,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        label.AddThemeColorOverride("font_color", Win98ThemeFactory.Shadow);
        return label;
    }

    private static void ConfigureToolButton(Button button, string text, string shortcut, string tooltip)
    {
        button.Text = $"{text}    {shortcut}";
        button.TooltipText = $"{tooltip} ({shortcut})";
        button.Alignment = HorizontalAlignment.Left;
        button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        button.CustomMinimumSize = new Vector2(108, 30);
    }

    private void StyleUnsavedPrompt()
    {
        if (GetTree().Root.FindChild("UnsavedChangesPrompt", true, false) is not PanelContainer panel)
            return;

        panel.CustomMinimumSize = new Vector2(390, 170);
        panel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 3));
        if (panel.GetChildCount() == 0 || panel.GetChild(0) is not VBoxContainer box)
            return;

        box.AddThemeConstantOverride("separation", 10);
        if (box.FindChild("UnsavedPromptTitleBar", false, false) is null)
        {
            var titlePanel = new PanelContainer { Name = "UnsavedPromptTitleBar" };
            titlePanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
            var title = new Label
            {
                Text = "Desktop Buddy",
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            title.AddThemeColorOverride("font_color", Win98ThemeFactory.Light);
            titlePanel.AddChild(title);
            box.AddChild(titlePanel);
            box.MoveChild(titlePanel, 0);
        }

        foreach (Node node in box.FindChildren("*", nameof(Button), true, false))
        {
            if (node is Button button)
                button.CustomMinimumSize = new Vector2(92, 30);
        }

        foreach (Node node in box.FindChildren("*", nameof(Label), true, false))
        {
            if (node is Label { Text: "Save changes before continuing?" } message)
            {
                message.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                message.HorizontalAlignment = HorizontalAlignment.Center;
            }
        }
    }

    private void BuildNewCharacterPrompt(Control uiRoot)
    {
        if (uiRoot.FindChild("Win98NewCharacterPrompt", false, false) is PanelContainer existing)
        {
            _newCharacterPanel = existing;
            return;
        }

        _modalBlocker = new ColorRect
        {
            Name = "Win98NewCharacterModalBlocker",
            Color = new Color(0, 0, 0, 0.35f),
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _modalBlocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        uiRoot.AddChild(_modalBlocker);

        _newCharacterPanel = new PanelContainer
        {
            Name = "Win98NewCharacterPrompt",
            Visible = false,
            ProcessMode = ProcessModeEnum.Always,
            CustomMinimumSize = new Vector2(420, 210),
        };
        _newCharacterPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _newCharacterPanel.OffsetLeft = -210;
        _newCharacterPanel.OffsetTop = -105;
        _newCharacterPanel.OffsetRight = 210;
        _newCharacterPanel.OffsetBottom = 105;
        _newCharacterPanel.AddThemeStyleboxOverride(
            "panel", Win98ThemeFactory.Raised(Win98ThemeFactory.Face, 3));
        uiRoot.AddChild(_newCharacterPanel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        _newCharacterPanel.AddChild(column);

        var titlePanel = new PanelContainer();
        titlePanel.AddThemeStyleboxOverride("panel", Win98ThemeFactory.Flat(Win98ThemeFactory.ActiveTitle));
        column.AddChild(titlePanel);
        var title = new Label { Text = "Create New Character" };
        title.AddThemeColorOverride("font_color", Win98ThemeFactory.Light);
        titlePanel.AddChild(title);

        _motivation = new Label
        {
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

        var actions = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.End,
        };
        column.AddChild(actions);
        var cancel = new Button
        {
            Text = "Cancel",
            CustomMinimumSize = new Vector2(92, 30),
        };
        cancel.Pressed += CloseNewCharacterPrompt;
        actions.AddChild(cancel);
        var create = new Button
        {
            Text = "Create",
            CustomMinimumSize = new Vector2(92, 30),
        };
        create.Pressed += CreateNamedCharacter;
        actions.AddChild(create);
    }

    private void OpenNewCharacterPrompt()
    {
        if (!GodotObject.IsInstanceValid(_newCharacterPanel) ||
            !GodotObject.IsInstanceValid(_newCharacterName) ||
            !GodotObject.IsInstanceValid(_motivation))
        {
            return;
        }

        string phrase = CatchPhrases[_random.RandiRange(0, CatchPhrases.Length - 1)];
        _motivation!.Text = $"Create your {phrase}!";
        _newCharacterName!.Text = string.Empty;
        _modalBlocker!.Visible = true;
        _newCharacterPanel!.Visible = true;
        _newCharacterPanel.MoveToFront();
        _newCharacterName.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CloseNewCharacterPrompt()
    {
        if (GodotObject.IsInstanceValid(_newCharacterPanel))
            _newCharacterPanel!.Visible = false;
        if (GodotObject.IsInstanceValid(_modalBlocker))
            _modalBlocker!.Visible = false;
        _replacementNewButton?.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void CreateNamedCharacter()
    {
        if (!GodotObject.IsInstanceValid(_newCharacterName))
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
            return;
        }

        _pendingNewName = null;
        _pendingPreviousCharacter = null;
        PolishLibrary();
    }

    private void ResolvePendingNamedCharacter()
    {
        if (string.IsNullOrWhiteSpace(_pendingNewName) ||
            _host!.Session.PendingAction != CharacterEditorPendingAction.None)
        {
            return;
        }

        CharacterDocument? document = _host.Session.WorkingDocument;
        if (document is not null && document.Id != _pendingPreviousCharacter &&
            string.Equals(document.DisplayName, "New Character", StringComparison.Ordinal))
        {
            _host.Session.Rename(_pendingNewName);
        }

        _pendingNewName = null;
        _pendingPreviousCharacter = null;
    }

    private void PolishLibrary()
    {
        if (!GodotObject.IsInstanceValid(_library) || !GodotObject.IsInstanceValid(_host))
            return;

        for (int index = 0; index < _library!.ItemCount; index++)
            _library.SetItemTooltip(index, string.Empty);

        CharacterDocument? working = _host!.Session.WorkingDocument;
        bool persistedOnPage = working is not null &&
            _host.Session.CurrentPage.Any(entry => entry.CharacterId == working.Id);
        bool needsTransient = working is not null && !persistedOnPage;
        int transientIndex = FindTransientIndex();

        if (needsTransient)
        {
            if (transientIndex < 0)
            {
                transientIndex = _library.AddItem(working!.DisplayName);
                _library.SetItemMetadata(transientIndex, "transient-working-character");
            }
            else if (_library.GetItemText(transientIndex) != working!.DisplayName)
            {
                _library.SetItemText(transientIndex, working.DisplayName);
            }

            _library.Select(transientIndex);
            _libraryHadTransient = true;
        }
        else if (transientIndex >= 0)
        {
            _library.RemoveItem(transientIndex);
            _libraryHadTransient = false;
        }
        else if (_libraryHadTransient)
        {
            _libraryHadTransient = false;
        }
    }

    private int FindTransientIndex()
    {
        if (!GodotObject.IsInstanceValid(_library))
            return -1;

        for (int index = 0; index < _library!.ItemCount; index++)
        {
            Variant metadata = _library.GetItemMetadata(index);
            if (metadata.VariantType == Variant.Type.String &&
                metadata.AsString() == "transient-working-character")
            {
                return index;
            }
        }

        return -1;
    }
}

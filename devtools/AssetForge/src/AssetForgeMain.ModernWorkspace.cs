using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

/// <summary>
/// Presentation-only workspace refactor. Existing generator controls and callbacks are re-used so
/// this pass changes information architecture and progressive disclosure without forking recipe or
/// generation behavior.
/// </summary>
public partial class AssetForgeMain
{
    private bool _modernWorkspaceInstalled;
    private OptionButton _categorySelector = null!;
    private Label _categorySummary = null!;

    private void EnsureModernWorkspaceUi()
    {
        if (_modernWorkspaceInstalled || !_bridgeThicknessUiInstalled || !GodotObject.IsInstanceValid(_preview)) return;
        InstallModernWorkspaceUi();
        _modernWorkspaceInstalled = true;
    }

    private void InstallModernWorkspaceUi()
    {
        if (GetNodeOrNull<VBoxContainer>("Root") is not VBoxContainer root) return;
        if (_preview.GetParent() is not PanelContainer previewPanel || previewPanel.GetParent() is not Container legacyBody) return;
        if (legacyBody.GetChildCount() < 1 || legacyBody.GetChild(0) is not ScrollContainer leftScroll) return;
        if (leftScroll.GetChildCount() < 1 || leftScroll.GetChild(0) is not VBoxContainer left) return;

        Node[] legacyLeftChildren = left.GetChildren().ToArray();
        Control? legacyToolbar = root.GetNodeOrNull<Control>("Toolbar");
        Control? legacyRepositoryTools = root.GetNodeOrNull<Control>("RepositoryTools");
        if (legacyToolbar is not null) legacyToolbar.Visible = false;
        if (legacyRepositoryTools is not null) legacyRepositoryTools.Visible = false;

        root.AddThemeConstantOverride("separation", 8);

        PanelContainer header = CreateModernPanel(10, 0.20f);
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        header.AddChild(headerRow);
        var titleStack = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titleStack.AddThemeConstantOverride("separation", 0);
        headerRow.AddChild(titleStack);
        titleStack.AddChild(ModernHeading("Asset Forge", 21));
        _presetLabel.Reparent(titleStack, false);
        _presetLabel.AddThemeColorOverride("font_color", new Color(0.65f, 0.69f, 0.74f));
        _presetLabel.AddThemeFontSizeOverride("font_size", 12);

        AddModernActionButton(headerRow, "Open Recipe", () => _openRecipeDialog.PopupCenteredRatio(0.65f));
        AddModernActionButton(headerRow, "Save Recipe", () => _saveRecipeDialog.PopupCenteredRatio(0.65f));
        if (legacyRepositoryTools is not null)
        {
            Button maintenance = AddModernActionButton(headerRow, "Maintenance", () =>
            {
                legacyRepositoryTools.Visible = !legacyRepositoryTools.Visible;
            });
            maintenance.TooltipText = "Show verification, regeneration and repository maintenance actions.";
        }
        Button generate = AddModernActionButton(headerRow, "Generate", GenerateWithBridgeThickness);
        generate.TooltipText = "Build deterministic geometry from the current recipe and source image.";
        Button export = AddModernActionButton(headerRow, "Export to Game", Export);
        export.Disabled = _generated is null;
        export.TooltipText = "Write the generated asset and trusted metadata into the game repository.";
        _export = export;

        root.AddChild(header);
        root.MoveChild(header, Math.Max(0, legacyToolbar?.GetIndex() ?? 0));

        var workspace = new HSplitContainer
        {
            Name = "ModernWorkspace",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        var centerAndInspector = new HSplitContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        root.AddChild(workspace);
        root.MoveChild(workspace, header.GetIndex() + 1);

        leftScroll.Reparent(workspace, false);
        leftScroll.CustomMinimumSize = new Vector2(290, 0);
        workspace.AddChild(centerAndInspector);
        previewPanel.Reparent(centerAndInspector, false);
        previewPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        previewPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var inspectorScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(330, 0),
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        centerAndInspector.AddChild(inspectorScroll);
        var inspector = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inspector.AddThemeConstantOverride("separation", 10);
        inspectorScroll.AddChild(inspector);

        BuildModernAssetSidebar(left);
        BuildModernCategoryInspector(inspector, left);
        BuildModernPreviewToolbar(previewPanel);
        BuildModernCompactFooter(root);

        foreach (Node node in legacyLeftChildren)
            if (GodotObject.IsInstanceValid(node) && node.GetParent() == left && node is Control control)
                control.Visible = false;

        legacyBody.QueueFree();
        ApplyModernComfortableSizing(root);
    }

    private void BuildModernAssetSidebar(VBoxContainer left)
    {
        left.AddThemeConstantOverride("separation", 10);

        VBoxContainer assetCard = CreateModernCard(left, "Asset", "Choose what you are authoring. Category-specific controls live in the Inspector.");
        _categorySelector = new OptionButton();
        int selected = 0;
        for (int i = 0; i < AuthoringTemplateCatalog.All.Count; i++)
        {
            AuthoringTemplateSpec spec = AuthoringTemplateCatalog.All[i];
            string family = spec.Family == AssetFamily.BuddyStudio ? "Buddy Studio" : "Environment";
            _categorySelector.AddItem($"{family} / {spec.DisplayName}{(spec.Implemented ? string.Empty : " · Planned")}");
            _categorySelector.GetPopup().SetItemDisabled(i, !spec.Implemented);
            if (spec.Id == AuthoringTemplateCatalog.GlassesId) selected = i;
        }
        _categorySelector.Select(selected);
        _categorySelector.ItemSelected += index => RefreshModernCategorySummary((int)index);
        assetCard.AddChild(_categorySelector);
        _categorySummary = ModernMutedLabel(string.Empty);
        _categorySummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        assetCard.AddChild(_categorySummary);
        RefreshModernCategorySummary(selected);
        _migratePreset.Reparent(assetCard, false);

        VBoxContainer sourceCard = CreateModernCard(left, "Source & template", "Draw over the guide, hide the guide layer, then import the clean 1024×1024 PNG.");
        MoveModernLabeled(_source, sourceCard, "Source PNG");
        var sourceActions = new HBoxContainer();
        sourceActions.AddThemeConstantOverride("separation", 6);
        sourceCard.AddChild(sourceActions);
        AddModernActionButton(sourceActions, "Import PNG", () => _sourceDialog.PopupCenteredRatio(0.72f));
        AddModernActionButton(sourceActions, "Save template", () => _templateDialog.PopupCenteredRatio(0.65f));

        VBoxContainer identityCard = CreateModernCard(left, "Identity", "Keep the common authoring flow simple; publishing metadata is available when needed.");
        MoveModernLabeled(_displayName, identityCard, "Display name");
        VBoxContainer publishing = CreateModernFoldout(identityCard, "Publishing details", false);
        MoveModernLabeled(_featureId, publishing, "Feature ID");
        MoveModernLabeled(_contentId, publishing, "Ownership content ID");
        MoveModernLabeled(_price, publishing, "Price (credits)");
        MoveModernLabeled(_sort, publishing, "Sort order");
    }

    private void BuildModernCategoryInspector(VBoxContainer inspector, VBoxContainer legacyLeft)
    {
        inspector.AddChild(ModernHeading("Inspector", 18));
        inspector.AddChild(ModernMutedLabel("Glasses · category settings"));

        VBoxContainer shape = CreateModernCard(inspector, "Shape", "Front-frame and authored bridge controls.");
        MoveModernLabeled(_frameThickness, shape, "Frame thickness");
        _bridgeThicknessLabel.Text = "Bridge thickness";
        MoveModernLabeled(_bridgeThickness, shape, "Bridge thickness");
        MoveModernLabeled(_depth, shape, "Depth");
        MoveModernLabeled(_roundness, shape, "Roundness");

        VBoxContainer temples = CreateModernCard(inspector, "Side arms", "Generated 3D temple geometry extending behind the front frame.");
        MoveModernLabeled(_templeThickness, temples, "Temple thickness");
        MoveModernLabeled(_templeLength, temples, "Temple length");
        MoveModernLabeled(_templeDrop, temples, "Temple drop");

        VBoxContainer appearance = CreateModernCard(inspector, "Appearance", "Color readability in preview and runtime without changing the source texture.");
        MoveModernLabeled(_lightingLevel, appearance, "Lighting level");

        CheckButton? advancedToggle = legacyLeft.GetChildren().OfType<CheckButton>()
            .FirstOrDefault(static button => string.Equals(button.Text, "Advanced", StringComparison.Ordinal));
        if (advancedToggle is not null)
        {
            int oldIndex = advancedToggle.GetIndex();
            VBoxContainer? advancedBody = oldIndex + 1 < legacyLeft.GetChildCount()
                ? legacyLeft.GetChild(oldIndex + 1) as VBoxContainer
                : null;
            VBoxContainer advancedCard = CreateModernCard(inspector, "Generator", "Lower-level deterministic controls. Most assets should not need these.");
            advancedToggle.Text = "Show advanced settings";
            advancedToggle.Reparent(advancedCard, false);
            if (advancedBody is not null) advancedBody.Reparent(advancedCard, false);
        }
    }

    private void BuildModernPreviewToolbar(PanelContainer previewPanel)
    {
        previewPanel.RemoveChild(_preview);
        var previewStack = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        previewStack.AddThemeConstantOverride("separation", 4);
        previewPanel.AddChild(previewStack);
        var previewBar = new HBoxContainer();
        previewBar.AddThemeConstantOverride("separation", 8);
        previewStack.AddChild(previewBar);
        previewBar.AddChild(ModernHeading("Preview", 15));
        previewBar.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _reference.Text = "Reference head";
        _reference.Reparent(previewBar, false);
        AddModernActionButton(previewBar, "Reset view", () => _preview.ResetView());
        previewStack.AddChild(_preview);
        _preview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _preview.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
    }

    private void BuildModernCompactFooter(VBoxContainer root)
    {
        var footer = new VBoxContainer();
        footer.AddThemeConstantOverride("separation", 2);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        footer.AddChild(row);
        _status.Reparent(row, false);
        _status.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _status.AddThemeFontSizeOverride("font_size", 12);
        var detailsToggle = new CheckButton { Text = "Technical details" };
        row.AddChild(detailsToggle);
        _hashes.Reparent(footer, false);
        _hashes.Visible = false;
        _hashes.AddThemeFontSizeOverride("font_size", 11);
        _hashes.AddThemeColorOverride("font_color", new Color(0.60f, 0.64f, 0.69f));
        detailsToggle.Toggled += visible => _hashes.Visible = visible;
        root.AddChild(footer);
    }

    private void RefreshModernCategorySummary(int index)
    {
        if (!GodotObject.IsInstanceValid(_categorySummary) || index < 0 || index >= AuthoringTemplateCatalog.All.Count) return;
        AuthoringTemplateSpec spec = AuthoringTemplateCatalog.All[index];
        _categorySummary.Text = spec.Implemented
            ? spec.Summary
            : $"{spec.Summary}\nPlanned category — template and generator will be enabled together when this slice is implemented.";
    }

    private static VBoxContainer CreateModernCard(Container parent, string title, string subtitle)
    {
        PanelContainer panel = CreateModernPanel(10, 0.16f);
        parent.AddChild(panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        box.AddChild(ModernHeading(title, 15));
        Label help = ModernMutedLabel(subtitle);
        help.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        box.AddChild(help);
        return box;
    }

    private static VBoxContainer CreateModernFoldout(Container parent, string title, bool expanded)
    {
        var wrapper = new VBoxContainer();
        wrapper.AddThemeConstantOverride("separation", 5);
        parent.AddChild(wrapper);
        var toggle = new CheckButton { Text = title, ButtonPressed = expanded };
        wrapper.AddChild(toggle);
        var body = new VBoxContainer { Visible = expanded };
        body.AddThemeConstantOverride("separation", 5);
        wrapper.AddChild(body);
        toggle.Toggled += visible => body.Visible = visible;
        return body;
    }

    private static void MoveModernLabeled(Control field, VBoxContainer target, string conciseLabel)
    {
        Label? label = field.GetIndex() > 0 ? field.GetParent().GetChild(field.GetIndex() - 1) as Label : null;
        if (label is not null)
        {
            label.Text = conciseLabel;
            label.Reparent(target, false);
        }
        field.Reparent(target, false);
    }

    private static PanelContainer CreateModernPanel(int padding, float brightness)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(brightness, brightness + 0.01f, brightness + 0.025f, 1f),
            BorderColor = new Color(0.27f, 0.29f, 0.33f, 1f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = padding,
            ContentMarginTop = padding,
            ContentMarginRight = padding,
            ContentMarginBottom = padding,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    private static Label ModernHeading(string text, int size)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", new Color(0.93f, 0.95f, 0.98f));
        return label;
    }

    private static Label ModernMutedLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", new Color(0.61f, 0.65f, 0.70f));
        return label;
    }

    private static Button AddModernActionButton(Container parent, string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 34) };
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    private static void ApplyModernComfortableSizing(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is LineEdit line) line.CustomMinimumSize = new Vector2(line.CustomMinimumSize.X, 34);
            else if (child is SpinBox spin) spin.CustomMinimumSize = new Vector2(spin.CustomMinimumSize.X, 34);
            else if (child is OptionButton option) option.CustomMinimumSize = new Vector2(option.CustomMinimumSize.X, 36);
            else if (child is Button button && button.CustomMinimumSize.Y < 32) button.CustomMinimumSize = new Vector2(button.CustomMinimumSize.X, 34);
            ApplyModernComfortableSizing(child);
        }
    }
}

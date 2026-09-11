using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// The Build palette (owner layout 2026-09-11): categories down the left with 3D-rendered icons,
/// the chosen category's parts, presets or wire colours in the middle, and on the right a 3D preview
/// over the chosen row's name, description and stats. Choosing a row chooses the tool; a link
/// preset's tuning can be adjusted with the Properties sliders before it is placed, and a placed
/// link can be clicked and retuned the same way.
/// </summary>
public partial class BuildModeController
{
    private const float LinkSelectRadius = 6.0f;
    private const int CategoryIconPixels = 40;

    private const string Structural = "Structural";
    private const string Mechanics = "Mechanics";
    private const string Devices = "Devices";
    private const string Ropes = "Ropes";
    private const string Hinges = "Hinges";
    private const string Welds = "Welds";
    private const string Wires = "Wires";

    // Props and Misc (in the owner's mock-up) arrive with the Creator; a category with nothing in
    // it would only be a dead button.
    private static readonly string[] Categories = [Structural, Mechanics, Devices, Ropes, Hinges, Welds, Wires];

    /// <summary>One palette row: a part, a link preset or a wire colour.</summary>
    private sealed record PaletteEntry(
        string Label,
        string Category,
        BuildTool Tool,
        SandboxPartDefinition? Part = null,
        SandboxLinkPreset? Preset = null,
        SandboxWireColor Wire = SandboxWireColor.Green);

    private readonly List<PaletteEntry> _entries = BuildEntries();
    private readonly List<int> _visible = [];
    private readonly Dictionary<string, Button> _categoryButtons = [];
    private readonly Dictionary<string, int> _lastInCategory = [];
    private string _category = Structural;
    private Win98GroupBox? _partsGroup;
    private Label? _detailsTitle;
    private VBoxContainer? _stats;
    private SandboxLinkId? _selectedLink;
    private (float Strength, float Elasticity, float Stiffness) _newLink = (1.0f, 0.1f, 0.0f);
    private SandboxWireColor _wireColor = SandboxWireColor.Green;

    private HSlider? _linkStrengthSlider;
    private HSlider? _linkStretchSlider;
    private HSlider? _linkStiffnessSlider;
    private Label? _linkStrengthReadout;
    private Label? _linkStretchReadout;
    private Label? _linkStiffnessReadout;

    /// <summary>The link being edited in the room, if any.</summary>
    public SandboxLinkId? SelectedLink => _selectedLink;

    /// <summary>The category whose rows the middle list shows.</summary>
    public string SelectedCategory => _category;

    private static List<PaletteEntry> BuildEntries()
    {
        var entries = new List<PaletteEntry>();
        // Weapon Trigger joins the palette with its own runtime; until then it would do nothing.
        foreach (SandboxPartDefinition definition in SandboxPartCatalogue.Definitions)
        {
            string? category = definition switch
            {
                { Device: SandboxDeviceKind.WeaponTrigger } => null,
                { Device: SandboxDeviceKind.Piston } or { Shape: SandboxPartShape.Circle } => Mechanics,
                { Device: SandboxDeviceKind.None } => Structural,
                _ => Devices,
            };
            if (category is not null)
                entries.Add(new PaletteEntry(definition.DisplayName, category, BuildTool.Parts, Part: definition));
        }
        foreach (SandboxLinkPreset preset in SandboxLinkPresets.All)
        {
            (string category, BuildTool tool) = preset.Kind switch
            {
                SandboxLinkKind.Rope => (Ropes, BuildTool.Rope),
                SandboxLinkKind.Hinge => (Hinges, BuildTool.Hinge),
                _ => (Welds, BuildTool.Weld),
            };
            entries.Add(new PaletteEntry(preset.Name, category, tool, Preset: preset));
        }
        entries.AddRange(Enum.GetValues<SandboxWireColor>()
            .Select(color => new PaletteEntry($"{color} Wire", Wires, BuildTool.Wire, Wire: color)));
        return entries;
    }

    private PaletteEntry? CurrentEntry() =>
        _selectedIndex >= 0 && _selectedIndex < _entries.Count ? _entries[_selectedIndex] : null;

    /// <summary>
    /// Switches tool. When the chosen row is not already one of that tool's, the first of them is
    /// chosen, so the hotkeys 1–5 land on a sensible preset.
    /// </summary>
    public void SetTool(BuildTool tool)
    {
        if (CurrentEntry()?.Tool != tool)
        {
            int index = _entries.FindIndex(entry => entry.Tool == tool);
            if (index >= 0)
            {
                SelectEntry(index);
                return;
            }
        }
        ApplyTool(tool);
    }

    /// <summary>Shows one category, returning to the row last chosen in it.</summary>
    public bool SelectCategory(string category)
    {
        int index = _lastInCategory.TryGetValue(category, out int last)
            ? last
            : _entries.FindIndex(entry => entry.Category == category);
        if (index < 0)
            return false;
        SelectEntry(index);
        return true;
    }

    /// <summary>Chooses one palette row, as clicking it does, switching category if it must.</summary>
    private void SelectEntry(int index)
    {
        if (index < 0 || index >= _entries.Count)
            return;
        _selectedIndex = index;
        PaletteEntry entry = _entries[index];
        _lastInCategory[entry.Category] = index;
        if (entry.Category != _category || _visible.Count == 0)
        {
            _category = entry.Category;
            RefreshPalette();
        }
        foreach ((string name, Button button) in _categoryButtons)
            button.SetPressedNoSignal(name == _category);
        int row = _visible.IndexOf(index);
        if (row >= 0 && _partList is not null)
        {
            _partList.Select(row);
            _partList.EnsureCurrentIsVisible();
        }
        if (entry.Preset is { } preset)
            _newLink = (preset.Strength, preset.Elasticity, preset.Stiffness);
        if (entry.Tool == BuildTool.Wire)
            _wireColor = entry.Wire;
        ApplyTool(entry.Tool);
        ShowSelectedPart();
        RefreshProperties();
    }

    /// <summary>Fills the middle list with the current category's rows.</summary>
    private void RefreshPalette()
    {
        _visible.Clear();
        for (int index = 0; index < _entries.Count; index++)
        {
            if (_entries[index].Category == _category)
                _visible.Add(index);
        }
        if (_partsGroup?.FindChild("GroupCaption", recursive: true, owned: false) is Label caption)
            caption.Text = $"Parts ({_category})";
        if (_partList is null)
            return;
        _partList.Clear();
        foreach (int index in _visible)
            _partList.AddItem(_entries[index].Label);
        int row = _visible.IndexOf(_selectedIndex);
        if (row >= 0)
            _partList.Select(row);
        ShowSelectedPart();
    }

    /// <summary>The left column: one button per category, its icon a miniature 3D render.</summary>
    private void BuildCategoryColumn(Container parent)
    {
        var group = new Win98GroupBox { Name = "BuildModeCategories" };
        group.Configure("Categories");
        parent.AddChild(group);
        var buttons = new ButtonGroup();
        foreach (string category in Categories)
        {
            var button = new Button
            {
                Name = $"BuildModeCategory{category}",
                Text = category,
                Icon = CategoryIcon(category),
                ToggleMode = true,
                ButtonGroup = buttons,
                Alignment = HorizontalAlignment.Left,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(150, 46),
                ButtonPressed = category == _category,
            };
            button.AddThemeConstantOverride("icon_max_width", CategoryIconPixels);
            button.AddThemeConstantOverride("h_separation", 8);
            button.Pressed += () => SelectCategory(category);
            group.Content.AddChild(button);
            _categoryButtons[category] = button;
        }
    }

    /// <summary>A category's icon: its most typical member rendered once, turned a little to show its depth.</summary>
    private Texture2D CategoryIcon(string category)
    {
        PaletteModel model = category switch
        {
            Structural => SandboxPaletteModels.ForPart(_entries.First(entry => entry.Category == Structural).Part!),
            Mechanics => SandboxPaletteModels.ForPart(_entries.First(entry => entry.Part?.Device == SandboxDeviceKind.Piston).Part!),
            Devices => SandboxPaletteModels.ForPart(_entries.First(entry => entry.Part?.Device == SandboxDeviceKind.Button).Part!),
            Ropes => SandboxPaletteModels.ForLink(SandboxLinkKind.Rope, 1.0f, 0.1f, 0.0f),
            Hinges => SandboxPaletteModels.ForLink(SandboxLinkKind.Hinge, 1.0f, 0.0f, 0.6f),
            Welds => SandboxPaletteModels.ForLink(SandboxLinkKind.Weld, 1.0f, 0.0f, 0.0f),
            _ => SandboxPaletteModels.Spool(),
        };
        if (category == Structural)
            model.Node.RotationDegrees = new Vector3(0.0f, 0.0f, 25.0f);
        if (category != Wires)
            model.Node.RotationDegrees += new Vector3(14.0f, -24.0f, 0.0f);
        var stage = new SandboxModelStage(0.06f, 4.0f)
        {
            Name = $"BuildModeIcon{category}",
            Size = new Vector2I(CategoryIconPixels, CategoryIconPixels),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
        };
        AddChild(stage);
        // Links are wide scenes; an icon frames only their working end (the pin, the seam, the hanging block).
        float crop = category switch
        {
            Structural => 0.8f,
            Ropes => 0.55f,
            Hinges => 0.5f,
            Welds => 0.75f,
            _ => 1.0f,
        };
        stage.Show(model with { Extent = model.Extent * crop });
        return stage.GetTexture();
    }

    /// <summary>The right column's text: the chosen row's name, what it is for, and its numbers.</summary>
    private void ShowSelectedPart()
    {
        PaletteEntry? entry = CurrentEntry();
        if (entry?.Part is { } definition)
            _preview?.Show(definition);
        else if (entry?.Preset is { } preset)
            _preview?.ShowLink(preset.Kind, _newLink.Strength, _newLink.Elasticity, _newLink.Stiffness);
        else if (entry is not null)
            _preview?.ShowWire(entry.Wire);

        if (_detailsTitle is not null)
            _detailsTitle.Text = entry?.Label ?? string.Empty;
        if (_description is not null)
        {
            _description.Text = entry switch
            {
                { Part: { } part } => part.Description,
                { Preset: { } link } => link.Kind switch
                {
                    SandboxLinkKind.Rope => $"{link.Description} Click a part, then another part or empty space.",
                    SandboxLinkKind.Hinge => $"{link.Description} Click where two parts overlap, or one part to pin it.",
                    _ => $"{link.Description} Click where two parts overlap.",
                },
                { } => "Carries a pulse from a Button or Timer to what it sets off; the colour is only for " +
                    "telling circuits apart. Click a sender, then a receiver.",
                _ => string.Empty,
            };
        }
        SetStats(entry switch
        {
            { Part: { } part } =>
            [
                ("material", $"Material: {part.Material}"),
                ("size", $"Size: {part.Width:0} × {part.Height:0}"),
                ("mass", $"Mass: {part.Mass:0.#}"),
            ],
            { Preset: { } link } => link.Kind switch
            {
                SandboxLinkKind.Rope => [("strength", $"Strength: {StrengthText(_newLink.Strength)}"), ("stretch", $"Stretch: {_newLink.Elasticity * 100.0f:0}%")],
                SandboxLinkKind.Hinge => [("strength", $"Strength: {StrengthText(_newLink.Strength)}"), ("stiffness", $"Stiffness: {StiffnessText(_newLink.Stiffness)}")],
                _ => [("strength", $"Strength: {StrengthText(_newLink.Strength)}")],
            },
            { } wire => [("color", $"Colour: {wire.Wire}"), ("timer", "Carries: one pulse at a time")],
            _ => [],
        });
    }

    private void SetStats((string Icon, string Text)[] rows)
    {
        if (_stats is null)
            return;
        foreach (Node child in _stats.GetChildren())
        {
            _stats.RemoveChild(child);
            child.QueueFree();
        }
        foreach ((string icon, string text) in rows)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(new TextureRect { Texture = BuildIcons.Get(icon), StretchMode = TextureRect.StretchModeEnum.KeepCentered, CustomMinimumSize = new Vector2(20, 20) });
            row.AddChild(new Label { Text = text });
            _stats.AddChild(row);
        }
    }

    private static string StrengthText(float strength) => strength >= 1.0f ? "Unbreakable" : $"{strength * 100.0f:0}%";

    private static string StiffnessText(float stiffness) => stiffness <= 0.0f ? "Free" : $"{stiffness * 100.0f:0}%";

    /// <summary>The link drawn under a point: a hinge or weld pin, or anywhere along a rope.</summary>
    private bool TryFindLinkAt(Vector2 world, float radius, out SandboxLink link)
    {
        link = null!;
        float best = radius;
        foreach (SandboxLink candidate in _sandbox.DocumentLinks)
        {
            if (_sandbox.LinkEndWorld(candidate.A) is not { } a || _sandbox.LinkEndWorld(candidate.B) is not { } b)
                continue;
            float distance = candidate.Kind == SandboxLinkKind.Rope
                ? DistanceToSegment(world, a, b)
                : world.DistanceTo(a);
            if (distance <= best)
            {
                best = distance;
                link = candidate;
            }
        }
        return link is not null;
    }

    /// <summary>Selects the link under a point, for tuning; false when there is none.</summary>
    public bool SelectLinkAt(Vector2 world)
    {
        if (!TryFindLinkAt(world, LinkSelectRadius, out SandboxLink link))
            return false;
        SelectLink(link.LinkId);
        SetStatus($"Selected a {link.Kind.ToString().ToLowerInvariant()}. Tune it below; Delete removes it.");
        return true;
    }

    private void SelectLink(SandboxLinkId? linkId)
    {
        if (linkId is not null && _selectedPart is not null)
            SelectPlacedPart(null);
        _selectedLink = linkId;
        _sandbox.HighlightedLink = linkId ?? default;
        RefreshProperties();
    }

    private bool TrySelectedLink(out SandboxLink link)
    {
        link = null!;
        if (_selectedLink is not { } id || !_scenes.ActiveSandbox.TryGetLink(id, out SandboxLink? found) || found is null)
            return false;
        link = found;
        return true;
    }

    /// <summary>Retunes the selected placed link, or the next link to be placed when none is selected.</summary>
    public bool TuneLink(float strength, float elasticity, float stiffness)
    {
        if (TrySelectedLink(out SandboxLink link))
        {
            if (!_scenes.ActiveSandbox.SetLinkTuning(link.LinkId, strength, elasticity, stiffness).Succeeded)
                return false;
            _sandbox.RebuildBuiltLinks();
        }
        else
        {
            _newLink = (Mathf.Clamp(strength, 0.0f, 1.0f), Mathf.Clamp(elasticity, 0.0f, 1.0f), Mathf.Clamp(stiffness, 0.0f, 1.0f));
            ShowSelectedPart();
        }
        RefreshProperties();
        return true;
    }

    private void DeleteSelection()
    {
        if (TrySelectedLink(out SandboxLink link))
        {
            _scenes.ActiveSandbox.RemoveLink(link.LinkId);
            SelectLink(null);
            _sandbox.RebuildBuiltLinks();
            SetStatus($"Removed a {link.Kind.ToString().ToLowerInvariant()}.");
            return;
        }
        DeleteSelectedPart();
    }

    /// <summary>The link rows of the Properties group: strength for every link, stretch for ropes, stiffness for hinges.</summary>
    private void BuildLinkPropertiesUi(VBoxContainer column)
    {
        (float Strength, float Elasticity, float Stiffness) Current() =>
            TrySelectedLink(out SandboxLink link) ? (link.Strength, link.Elasticity, link.Stiffness) : _newLink;

        _linkStrengthSlider = AddPropertySlider(column, "Strength", "strength", "How much it takes to break it. All the way is unbreakable.",
            0.0, 1.0, 0.05, out _linkStrengthReadout, value =>
            {
                var current = Current();
                TuneLink((float)value, current.Elasticity, current.Stiffness);
            });
        _linkStretchSlider = AddPropertySlider(column, "Stretch", "stretch", "How far the rope stretches under load before it pulls back.",
            0.0, 1.0, 0.05, out _linkStretchReadout, value =>
            {
                var current = Current();
                TuneLink(current.Strength, (float)value, current.Stiffness);
            });
        _linkStiffnessSlider = AddPropertySlider(column, "Stiffness", "stiffness", "How hard the hinge resists turning. None swings freely.",
            0.0, 1.0, 0.05, out _linkStiffnessReadout, value =>
            {
                var current = Current();
                TuneLink(current.Strength, current.Elasticity, (float)value);
            });
    }

    /// <summary>
    /// Shows the link rows when a placed link is selected, or when a link preset is chosen and no
    /// part is selected; false when the Properties group is about a part instead.
    /// </summary>
    private bool RefreshLinkProperties()
    {
        SandboxLinkKind kind;
        (float Strength, float Elasticity, float Stiffness) tuning;
        string title;
        if (TrySelectedLink(out SandboxLink link))
        {
            kind = link.Kind;
            tuning = (link.Strength, link.Elasticity, link.Stiffness);
            title = $"Selected {kind.ToString().ToLowerInvariant()} in the room.";
        }
        else if (_selectedPart is null && CurrentEntry()?.Preset is { } preset)
        {
            kind = preset.Kind;
            tuning = _newLink;
            title = $"The next {preset.Name.ToLowerInvariant()} you place.";
        }
        else
        {
            foreach (HSlider? slider in new[] { _linkStrengthSlider, _linkStretchSlider, _linkStiffnessSlider })
                Row(slider!).Visible = false;
            return false;
        }

        _propertiesTitle!.Text = title;
        Row(_linkStrengthSlider!).Visible = true;
        Row(_linkStretchSlider!).Visible = kind == SandboxLinkKind.Rope;
        Row(_linkStiffnessSlider!).Visible = kind == SandboxLinkKind.Hinge;
        _linkStrengthSlider!.SetValueNoSignal(tuning.Strength);
        _linkStretchSlider!.SetValueNoSignal(tuning.Elasticity);
        _linkStiffnessSlider!.SetValueNoSignal(tuning.Stiffness);
        _linkStrengthReadout!.Text = StrengthText(tuning.Strength);
        _linkStretchReadout!.Text = $"{tuning.Elasticity * 100.0f:0}%";
        _linkStiffnessReadout!.Text = StiffnessText(tuning.Stiffness);
        return true;
    }

    /// <summary>The row a slider sits in: icon, name, slider, value.</summary>
    private static Control Row(Control slider) => slider.GetParent<Control>();
}

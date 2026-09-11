using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Sandbox;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// The Build palette as one list (owner note 2026-09-11): parts, then rope, hinge and weld presets,
/// then wire colours, under headings. Choosing a row chooses the tool; a link preset's tuning can be
/// adjusted with the Properties sliders before it is placed, and a placed link can be clicked and
/// retuned the same way.
/// </summary>
public partial class BuildModeController
{
    private const float LinkSelectRadius = 6.0f;

    /// <summary>One palette row: a heading, a part, a link preset or a wire colour.</summary>
    private sealed record PaletteEntry(
        string Label,
        BuildTool Tool,
        SandboxPartDefinition? Part = null,
        SandboxLinkPreset? Preset = null,
        SandboxWireColor Wire = SandboxWireColor.Green,
        bool IsHeader = false);

    private readonly List<PaletteEntry> _entries = BuildEntries();
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

    private static List<PaletteEntry> BuildEntries()
    {
        var entries = new List<PaletteEntry> { new("Parts", BuildTool.Parts, IsHeader: true) };
        // Weapon Trigger joins the palette with its own runtime; until then it would do nothing.
        entries.AddRange(SandboxPartCatalogue.Definitions
            .Where(definition => definition.Device is not SandboxDeviceKind.WeaponTrigger)
            .Select(definition => new PaletteEntry(definition.DisplayName, BuildTool.Parts, Part: definition)));
        foreach ((SandboxLinkKind kind, string heading, BuildTool tool) in new[]
                 {
                     (SandboxLinkKind.Rope, "Ropes", BuildTool.Rope),
                     (SandboxLinkKind.Hinge, "Hinges", BuildTool.Hinge),
                     (SandboxLinkKind.Weld, "Welds", BuildTool.Weld),
                 })
        {
            entries.Add(new PaletteEntry(heading, tool, IsHeader: true));
            entries.AddRange(SandboxLinkPresets.All
                .Where(preset => preset.Kind == kind)
                .Select(preset => new PaletteEntry(preset.Name, tool, Preset: preset)));
        }
        entries.Add(new PaletteEntry("Wires", BuildTool.Wire, IsHeader: true));
        entries.AddRange(Enum.GetValues<SandboxWireColor>()
            .Select(color => new PaletteEntry($"{color} Wire", BuildTool.Wire, Wire: color)));
        return entries;
    }

    private PaletteEntry? CurrentEntry() =>
        _selectedIndex >= 0 && _selectedIndex < _entries.Count && !_entries[_selectedIndex].IsHeader
            ? _entries[_selectedIndex]
            : null;

    /// <summary>
    /// Switches tool. When the chosen row is not already one of that tool's, the first of them is
    /// chosen, so the hotkeys 1–5 land on a sensible preset.
    /// </summary>
    public void SetTool(BuildTool tool)
    {
        if (CurrentEntry()?.Tool != tool)
        {
            int index = _entries.FindIndex(entry => !entry.IsHeader && entry.Tool == tool);
            if (index >= 0)
            {
                SelectEntry(index);
                return;
            }
        }
        ApplyTool(tool);
    }

    /// <summary>Chooses one palette row, as clicking it does; headings cannot be chosen.</summary>
    private void SelectEntry(int index)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].IsHeader)
        {
            _partList?.Select(Math.Clamp(_selectedIndex, 0, Math.Max(0, _entries.Count - 1)));
            return;
        }
        _selectedIndex = index;
        _partList?.Select(index);
        _partList?.EnsureCurrentIsVisible();
        PaletteEntry entry = _entries[index];
        if (entry.Preset is { } preset)
            _newLink = (preset.Strength, preset.Elasticity, preset.Stiffness);
        if (entry.Tool == BuildTool.Wire)
            _wireColor = entry.Wire;
        ApplyTool(entry.Tool);
        ShowSelectedPart();
        RefreshProperties();
    }

    private void RefreshPalette()
    {
        if (_partList is null)
            return;

        _partList.Clear();
        foreach (PaletteEntry entry in _entries)
        {
            int row = _partList.AddItem(entry.IsHeader ? entry.Label.ToUpperInvariant() : "   " + entry.Label);
            if (entry.IsHeader)
            {
                _partList.SetItemSelectable(row, false);
                _partList.SetItemCustomFgColor(row, new Color("000080"));
            }
        }
        if (CurrentEntry() is null)
            _selectedIndex = _entries.FindIndex(entry => !entry.IsHeader);
        _partList.Select(_selectedIndex);
        ShowSelectedPart();
    }

    private void ShowSelectedPart()
    {
        PaletteEntry? entry = CurrentEntry();
        if (entry?.Part is { } definition)
            _preview?.Show(definition);
        else if (entry?.Preset is { } preset)
            _preview?.ShowLink(preset.Kind, _newLink.Strength, _newLink.Elasticity, _newLink.Stiffness);
        else if (entry is not null)
            _preview?.ShowWire(entry.Wire);
        if (_description is null)
            return;
        _description.Text = entry switch
        {
            { Part: { } part } =>
                $"{part.Description}\n{part.Material} · {part.Width:0}×{part.Height:0} · mass {part.Mass:0.#}",
            { Preset: { } link } => link.Kind switch
            {
                SandboxLinkKind.Rope => $"{link.Description}\nClick a part, then another part or empty space.",
                SandboxLinkKind.Hinge => $"{link.Description}\nClick where two parts overlap, or on one part to pin it.",
                _ => $"{link.Description}\nClick where two parts overlap.",
            },
            { } => "Carries a pulse from a Button or Timer to what it sets off. The colour is only " +
                "for telling circuits apart.\nClick a sender, then a receiver.",
            _ => string.Empty,
        };
    }

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

        _linkStrengthSlider = AddPropertySlider(column, "Strength", "How much it takes to break it. All the way is unbreakable.",
            0.0, 1.0, 0.05, out _linkStrengthReadout, value =>
            {
                var current = Current();
                TuneLink((float)value, current.Elasticity, current.Stiffness);
            });
        _linkStretchSlider = AddPropertySlider(column, "Stretch", "How far the rope stretches under load before it pulls back.",
            0.0, 1.0, 0.05, out _linkStretchReadout, value =>
            {
                var current = Current();
                TuneLink(current.Strength, (float)value, current.Stiffness);
            });
        _linkStiffnessSlider = AddPropertySlider(column, "Stiffness", "How hard the hinge resists turning. None swings freely.",
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
            title = $"Selected {kind.ToString().ToLowerInvariant()}";
        }
        else if (_selectedPart is null && CurrentEntry()?.Preset is { } preset)
        {
            kind = preset.Kind;
            tuning = _newLink;
            title = $"Next {preset.Name.ToLowerInvariant()} you place";
        }
        else
        {
            foreach (HSlider? slider in new[] { _linkStrengthSlider, _linkStretchSlider, _linkStiffnessSlider })
                slider!.GetParent<Control>().Visible = false;
            return false;
        }

        _propertiesTitle!.Text = title;
        Row(_linkStrengthSlider!).Visible = true;
        Row(_linkStretchSlider!).Visible = kind == SandboxLinkKind.Rope;
        Row(_linkStiffnessSlider!).Visible = kind == SandboxLinkKind.Hinge;
        _linkStrengthSlider!.SetValueNoSignal(tuning.Strength);
        _linkStretchSlider!.SetValueNoSignal(tuning.Elasticity);
        _linkStiffnessSlider!.SetValueNoSignal(tuning.Stiffness);
        _linkStrengthReadout!.Text = tuning.Strength >= 1.0f ? "Unbreakable" : $"{tuning.Strength * 100.0f:0}%";
        _linkStretchReadout!.Text = $"{tuning.Elasticity * 100.0f:0}%";
        _linkStiffnessReadout!.Text = tuning.Stiffness <= 0.0f ? "Free" : $"{tuning.Stiffness * 100.0f:0}%";
        return true;
    }

    private static Control Row(Control slider) => slider.GetParent<Control>();
}

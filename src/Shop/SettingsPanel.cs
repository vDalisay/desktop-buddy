using System;
using System.Collections.Generic;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.Shop;

/// <summary>
/// The Settings window: one row per action, in the same chrome as the shop and tool picker.
/// It owns no settings logic — each row is a label and a button the composition root supplies,
/// so the FR-003.2 entries (Character Editor today, Reset Progress next) stay where they live.
/// </summary>
public partial class SettingsPanel : PanelContainer
{
    private VBoxContainer _list = null!;
    private Label _status = null!;

    public bool IsInitialized { get; private set; }

    public void Configure()
    {
        Name = "SettingsPanel";
        PanelChrome.Parts parts = PanelChrome.Build(this, "Settings", "SettingsActionList");
        _list = parts.List;
        _status = parts.Status;
        _status.Text = "Changes apply immediately.";
        IsInitialized = true;
    }

    /// <summary>Adds one labelled action row and returns its button.</summary>
    public Button AddAction(string label, string description, Action pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        var button = new Button { Text = "Open" };
        button.Pressed += pressed;
        PanelChrome.Row(_list, label, new Label(), button);
        button.TooltipText = description;
        _actions.Add(label, button);
        return button;
    }

    /// <summary>The action button for one row (test observability).</summary>
    public Button? ActionFor(string label) =>
        _actions.TryGetValue(label, out Button? button) ? button : null;

    private readonly Dictionary<string, Button> _actions = new(StringComparer.Ordinal);
}

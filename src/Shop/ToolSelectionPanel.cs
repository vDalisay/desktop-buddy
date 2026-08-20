using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using DesktopBuddy.UI;
using DesktopBuddy.Ui;
using Godot;

namespace DesktopBuddy.Shop;

/// <summary>
/// Picks the active tool (FR-019). It offers every selectable catalogue entry that has passed
/// its gate; unowned ones show their shop price and stay disabled, so the picker doubles as
/// the "what am I saving for" view. Selection routes through the pipeline's single
/// <see cref="InteractionDamageComponent.SelectTool"/> seam — this panel owns no rules.
/// </summary>
public partial class ToolSelectionPanel : PanelContainer
{
    private readonly List<Row> _rows = [];
    private BuddyProgressState _progress = null!;
    private InteractionDamageComponent _pipeline = null!;
    private Label _selected = null!;
    private Label _status = null!;

    public bool IsInitialized { get; private set; }
    public int SelectionCount { get; private set; }

    public void Configure(
        BuddyProgressState progress,
        InteractionDamageComponent pipeline,
        ToolCatalogue catalogue)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        ArgumentNullException.ThrowIfNull(catalogue);

        Name = "ToolSelectionPanel";
        PanelChrome.Parts parts = PanelChrome.Build(this, "Tools", "ToolSelectionList");
        _selected = parts.HeaderValue;
        _status = parts.Status;

        foreach (CatalogueEntry entry in CataloguePolicy.SelectableEntries(catalogue))
        {
            if (ContentIds.TryParseTool(entry.ContentId, out ToolId tool))
                _rows.Add(BuildRow(parts.List, entry, tool));
        }

        IsInitialized = true;
        Refresh();
    }

    private Row BuildRow(VBoxContainer list, CatalogueEntry entry, ToolId tool)
    {
        var select = new Button { Text = "Equip" };
        select.Pressed += () => Select(entry.ContentId, tool);
        UiFeedbackAudioBootstrap.Tag(select, layer: UiSfx.NoLayer);
        var price = new Label();
        PanelChrome.Row(list, ContentDisplayName.For(entry.ContentId), price, select);
        return new Row(entry, tool, select, price);
    }

    private void Select(string contentId, ToolId tool)
    {
        _pipeline.SelectTool(tool);
        string name = ContentDisplayName.For(contentId);
        bool applied = _progress.SelectedTool == tool;
        _status.Text = applied
            ? $"{name} equipped."
            : $"{name} could not be equipped — buy it in the shop first.";
        if (applied)
        {
            SelectionCount++;
            UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Equip);
        }
        Refresh();
    }

    /// <summary>The select control for one tool row (test observability).</summary>
    public Button? SelectButtonFor(string contentId)
    {
        foreach (Row row in _rows)
        {
            if (string.Equals(row.Entry.ContentId, contentId, StringComparison.Ordinal))
                return row.Select;
        }

        return null;
    }

    public IReadOnlyList<string> OfferedContentIds =>
        _rows.ConvertAll(static row => row.Entry.ContentId);

    /// <summary>Re-reads ownership and the active tool; call whenever the window opens.</summary>
    public void Refresh()
    {
        if (!IsInitialized)
            return;

        _selected.Text = ContentDisplayName.For(ContentIds.ForTool(_progress.SelectedTool));
        foreach (Row row in _rows)
        {
            bool owned = row.Entry.IsStarting ||
                _progress.IsToolUnlocked(row.Entry.ContentId);
            bool active = _progress.SelectedTool == row.Tool;
            string name = ContentDisplayName.For(row.Entry.ContentId);
            string price = ContentDisplayName.Credits(row.Entry.PriceMilliCredits);

            row.Price.Text = owned ? string.Empty : price;
            row.Select.Text = active ? "Equipped" : "Equip";
            row.Select.Disabled = !owned || active;
            row.Select.TooltipText = active
                ? $"{name} is currently equipped."
                : owned
                    ? $"Equip {name}."
                    : $"Buy {name} in the Shop for {price} before equipping it.";
            row.Select.TooltipText = ContentDisplayName.WithUsage(
                row.Select.TooltipText, row.Entry.ContentId);
        }
    }

    private readonly record struct Row(
        CatalogueEntry Entry,
        ToolId Tool,
        Button Select,
        Label Price);
}
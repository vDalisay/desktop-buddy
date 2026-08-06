using System;
using System.Linq;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Keeps the current unsaved new buddy visible as a selected character-list entry.</summary>
public partial class Win98PaintTransientCharacterBootstrap : Node
{
    private const string Marker = "win98-transient-character";

    private CharacterEditorHost? _host;
    private ItemList? _list;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _host ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        _list ??= GetTree().Root.FindChild("CharacterLibraryList", true, false) as ItemList;
        if (!GodotObject.IsInstanceValid(_host) || !GodotObject.IsInstanceValid(_list) || !_host!.IsEditorOpen)
            return;

        CharacterDocument? working = _host.Session.WorkingDocument;
        bool persisted = working is not null &&
            _host.Session.CurrentPage.Any(entry => entry.CharacterId == working.Id);
        int transient = FindTransient();

        if (working is not null && !persisted)
        {
            if (transient < 0)
            {
                transient = _list!.AddItem(working.DisplayName);
                _list.SetItemMetadata(transient, Marker);
            }
            else if (_list!.GetItemText(transient) != working.DisplayName)
            {
                _list.SetItemText(transient, working.DisplayName);
            }
            _list.SetItemTooltip(transient, string.Empty);
            _list.Select(transient);
        }
        else if (transient >= 0)
        {
            _list!.RemoveItem(transient);
        }
    }

    private int FindTransient()
    {
        for (int index = 0; index < _list!.ItemCount; index++)
        {
            Variant metadata = _list.GetItemMetadata(index);
            if (metadata.VariantType == Variant.Type.String &&
                string.Equals(metadata.AsString(), Marker, StringComparison.Ordinal))
                return index;
        }
        return -1;
    }
}

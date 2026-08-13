using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using Godot;

namespace DesktopBuddy.Content;

[GlobalClass]
public partial class GeneratedCatalogueDefinition : GameResource
{
    [Export] public Godot.Collections.Array<ToolDefinition> Entries { get; set; } = [];

    public ToolCatalogue ToCatalogue() => new(Entries.Select(static definition => definition.ToEntry()));

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        var domainEntries = new List<CatalogueEntry>();
        foreach (ToolDefinition? definition in Entries)
        {
            if (!GodotObject.IsInstanceValid(definition)) { errors.Add("Generated catalogue contains a null definition."); continue; }
            foreach (string error in definition.Validate()) errors.Add(error);
            CatalogueEntry entry = definition.ToEntry();
            if (entry.Kind != CatalogueEntryKind.Cosmetic) errors.Add($"Generated catalogue entry '{entry.ContentId}' must be a cosmetic.");
            if (entry.ProgressionOrder < 10000) errors.Add($"Generated catalogue entry '{entry.ContentId}' must use the reserved generated order range.");
            domainEntries.Add(entry);
        }
        foreach (string error in ToolCatalogue.Validate(domainEntries)) errors.Add(error);
        return errors;
    }
}

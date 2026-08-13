using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

[GlobalClass]
public partial class GeneratedBuddyCosmeticCatalogueResource : GameResource
{
    [Export] public Godot.Collections.Array<GeneratedBuddyCosmeticResource> Entries { get; set; } = [];

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var contentIds = new HashSet<string>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        foreach (GeneratedBuddyCosmeticResource? entry in Entries)
        {
            if (!GodotObject.IsInstanceValid(entry)) { errors.Add("Generated cosmetic catalogue contains a null entry."); continue; }
            foreach (string error in entry.Validate()) errors.Add(error);
            if (!ids.Add(entry.FeatureId)) errors.Add($"Duplicate generated feature ID '{entry.FeatureId}'.");
            if (!contentIds.Add(entry.ContentId)) errors.Add($"Duplicate generated content ID '{entry.ContentId}'.");
            if (!orders.Add(entry.SortOrder)) errors.Add($"Duplicate generated sort order {entry.SortOrder}.");
        }
        return errors;
    }
}

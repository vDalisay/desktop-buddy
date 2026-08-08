using System;
using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

[GlobalClass]
public partial class EnvironmentDecorationCatalogueResource : GameResource
{
    [Export] public Godot.Collections.Array<EnvironmentDecorationResource> Entries { get; set; } = new();

    public DecorationCatalogue ToCatalogue()
    {
        var definitions = new List<DecorationDefinition>(Entries.Count);
        foreach (EnvironmentDecorationResource entry in Entries)
            if (GodotObject.IsInstanceValid(entry)) definitions.Add(entry.ToDefinition());
        return new DecorationCatalogue(definitions);
    }

    public EnvironmentDecorationResource? Find(DecorationDefinitionId id)
    {
        foreach (EnvironmentDecorationResource entry in Entries)
            if (GodotObject.IsInstanceValid(entry) && string.Equals(entry.DefinitionId, id.Value, StringComparison.Ordinal)) return entry;
        return null;
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        foreach (EnvironmentDecorationResource entry in Entries)
        {
            if (!GodotObject.IsInstanceValid(entry)) { errors.Add("Environment catalogue contains an empty entry."); continue; }
            foreach (string error in entry.Validate()) errors.Add(error);
        }
        try { _ = ToCatalogue(); }
        catch (Exception exception) { errors.Add(exception.Message); }
        return errors;
    }
}

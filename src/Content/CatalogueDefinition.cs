using System.Collections.Generic;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Content;
using Godot;

namespace DesktopBuddy.Content;

/// <summary>
/// The authored launch catalogue: one explicitly referenced list of
/// <see cref="ToolDefinition"/> entries rather than a scanned directory, so the shipped set
/// is deterministic, reviewable in one file, and validated as a whole at startup
/// (ARCHITECTURE §6).
/// </summary>
[GlobalClass]
public partial class CatalogueDefinition : GameResource
{
    [Export] public Godot.Collections.Array<ToolDefinition> Entries { get; set; } = new();

    /// <summary>Builds the validated domain snapshot; throws when the data is invalid.</summary>
    public ToolCatalogue ToCatalogue()
    {
        var entries = new List<CatalogueEntry>(Entries.Count);
        foreach (ToolDefinition definition in Entries)
        {
            if (GodotObject.IsInstanceValid(definition))
                entries.Add(definition.ToEntry());
        }

        return new ToolCatalogue(entries);
    }

    /// <summary>The authored definition for one ID, for the engine references it carries.</summary>
    public ToolDefinition? Find(string contentId)
    {
        foreach (ToolDefinition definition in Entries)
        {
            if (GodotObject.IsInstanceValid(definition) && definition.ContentId == contentId)
                return definition;
        }

        return null;
    }

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        var entries = new List<CatalogueEntry>(Entries.Count);

        for (int index = 0; index < Entries.Count; index++)
        {
            ToolDefinition definition = Entries[index];
            if (!GodotObject.IsInstanceValid(definition))
            {
                errors.Add($"catalogue slot {index} has no definition assigned");
                continue;
            }

            foreach (string error in definition.ValidateAssets())
            {
                errors.Add(error);
            }

            entries.Add(definition.ToEntry());
        }

        foreach (string error in ToolCatalogue.Validate(entries))
        {
            errors.Add(error);
        }

        if (errors.Count > 0)
        {
            // Launch completeness reads a constructed catalogue, which structural errors
            // would prevent; report those first rather than masking them behind a throw.
            return errors;
        }

        foreach (string error in CataloguePolicy.ValidateLaunchCatalogue(new ToolCatalogue(entries)))
        {
            errors.Add(error);
        }

        return errors;
    }
}

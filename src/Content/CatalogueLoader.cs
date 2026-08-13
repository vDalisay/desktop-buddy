using System;
using System.Linq;
using DesktopBuddy.Domain.Content;
using Godot;

namespace DesktopBuddy.Content;

/// <summary>
/// Loads the hand-authored launch catalogue and the separate Asset Forge-generated cosmetic
/// catalogue. Each boundary validates independently; only then are their domain entries merged.
/// </summary>
public static class CatalogueLoader
{
    public const string CataloguePath = "res://data/catalogue/launch_catalogue.tres";
    public const string GeneratedCataloguePath = "res://data/catalogue/generated_cosmetics.tres";

    private static CatalogueDefinition? _definition;
    private static GeneratedCatalogueDefinition? _generatedDefinition;
    private static ToolCatalogue? _catalogue;

    /// <summary>The authored launch definition. Launch policy/count applies only here.</summary>
    public static CatalogueDefinition Definition =>
        _definition ??= GD.Load<CatalogueDefinition>(CataloguePath)
            ?? throw new InvalidOperationException($"Missing or invalid catalogue resource at {CataloguePath}.");

    public static GeneratedCatalogueDefinition GeneratedDefinition =>
        _generatedDefinition ??= GD.Load<GeneratedCatalogueDefinition>(GeneratedCataloguePath)
            ?? throw new InvalidOperationException($"Missing generated catalogue resource at {GeneratedCataloguePath}.");

    public static ToolCatalogue Catalogue => _catalogue ??= LoadMerged();

    public static ToolDefinition? FindDefinition(string contentId)
    {
        ToolDefinition? launch = Definition.Find(contentId);
        if (launch is not null) return launch;
        foreach (ToolDefinition definition in GeneratedDefinition.Entries)
            if (GodotObject.IsInstanceValid(definition) && string.Equals(definition.ContentId, contentId, StringComparison.Ordinal))
                return definition;
        return null;
    }

    private static ToolCatalogue LoadMerged()
    {
        Godot.Collections.Array<string> generatedErrors = GeneratedDefinition.Validate();
        if (generatedErrors.Count > 0)
            throw new InvalidOperationException($"Invalid generated cosmetic catalogue: {string.Join("; ", generatedErrors)}");
        ToolCatalogue launch = Definition.ToCatalogue();
        ToolCatalogue generated = GeneratedDefinition.ToCatalogue();
        return new ToolCatalogue(launch.Entries.Concat(generated.Entries));
    }
}

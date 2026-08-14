using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

public static class EnvironmentDecorationRegistry
{
    public const string CataloguePath = "res://data/environment/launch_decorations.tres";
    public const string GeneratedCataloguePath = "res://data/environment/generated_decorations.tres";
    private static EnvironmentDecorationCatalogueResource? _catalogue;
    private static EnvironmentDecorationCatalogueResource? _generatedCatalogue;

    /// <summary>Hand-authored launch catalogue. Launch-content tests intentionally inspect this boundary alone.</summary>
    public static EnvironmentDecorationCatalogueResource Authored =>
        _catalogue ??= GD.Load<EnvironmentDecorationCatalogueResource>(CataloguePath)
            ?? throw new InvalidOperationException($"Missing Environment catalogue at {CataloguePath}.");

    /// <summary>Asset Forge-owned catalogue. A clean checkout contains a valid empty aggregate.</summary>
    public static EnvironmentDecorationCatalogueResource Generated =>
        _generatedCatalogue ??= GD.Load<EnvironmentDecorationCatalogueResource>(GeneratedCataloguePath)
            ?? throw new InvalidOperationException($"Missing generated Environment catalogue at {GeneratedCataloguePath}.");

    public static IEnumerable<EnvironmentDecorationResource> Entries =>
        Authored.Entries.Concat(Generated.Entries)
            .Where(static entry => GodotObject.IsInstanceValid(entry));

    public static DecorationCatalogue Domain
    {
        get
        {
            ValidateGeneratedBoundary();
            return new DecorationCatalogue(Entries.Select(static entry => entry.ToDefinition()));
        }
    }

    public static EnvironmentDecorationResource? Find(DecorationDefinitionId id)
    {
        EnvironmentDecorationResource? authored = Authored.Find(id);
        if (authored is not null) return authored;
        ValidateGeneratedBoundary();
        return Generated.Find(id);
    }

    private static void ValidateGeneratedBoundary()
    {
        Godot.Collections.Array<string> errors = Generated.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid generated Environment catalogue: {string.Join("; ", errors)}");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (EnvironmentDecorationResource entry in Authored.Entries)
            if (GodotObject.IsInstanceValid(entry)) seen.Add(entry.DefinitionId);
        foreach (EnvironmentDecorationResource entry in Generated.Entries)
        {
            if (!GodotObject.IsInstanceValid(entry)) continue;
            if (!seen.Add(entry.DefinitionId))
                throw new InvalidOperationException($"Generated Environment definition '{entry.DefinitionId}' collides with an existing trusted definition.");
        }
    }
}

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
    private static EnvironmentDecorationCatalogueResource? _launchCatalogue;
    private static EnvironmentDecorationCatalogueResource? _generatedCatalogue;
    private static EnvironmentDecorationCatalogueResource? _composedCatalogue;
    private static bool _runtimeCompositionActivated;

    /// <summary>Hand-authored launch catalogue only.</summary>
    public static EnvironmentDecorationCatalogueResource Launch =>
        _launchCatalogue ??= GD.Load<EnvironmentDecorationCatalogueResource>(CataloguePath)
            ?? throw new InvalidOperationException($"Missing Environment catalogue at {CataloguePath}.");

    /// <summary>Asset Forge-owned catalogue. A clean checkout contains a valid empty aggregate.</summary>
    public static EnvironmentDecorationCatalogueResource Generated =>
        _generatedCatalogue ??= GD.Load<EnvironmentDecorationCatalogueResource>(GeneratedCataloguePath)
            ?? throw new InvalidOperationException($"Missing generated Environment catalogue at {GeneratedCataloguePath}.");

    /// <summary>
    /// Historical public resource seam. Before runtime editing is activated it intentionally
    /// remains launch-only so launch-policy tests inspect the original authored boundary. Once the
    /// live domain catalogue is requested, the Room Decorator sees the validated composed resource
    /// through this same legacy seam and therefore needs no duplicated browse/purchase code.
    /// </summary>
    public static EnvironmentDecorationCatalogueResource Authored =>
        _runtimeCompositionActivated ? Runtime : Launch;

    public static EnvironmentDecorationCatalogueResource Runtime =>
        _composedCatalogue ??= Compose();

    public static IEnumerable<EnvironmentDecorationResource> Entries => Runtime.Entries;

    public static DecorationCatalogue Domain
    {
        get
        {
            _runtimeCompositionActivated = true;
            return Runtime.ToCatalogue();
        }
    }

    public static EnvironmentDecorationResource? Find(DecorationDefinitionId id) => Runtime.Find(id);

    private static EnvironmentDecorationCatalogueResource Compose()
    {
        Godot.Collections.Array<string> launchErrors = Launch.Validate();
        if (launchErrors.Count > 0)
            throw new InvalidOperationException($"Invalid launch Environment catalogue: {string.Join("; ", launchErrors)}");
        Godot.Collections.Array<string> generatedErrors = Generated.Validate();
        if (generatedErrors.Count > 0)
            throw new InvalidOperationException($"Invalid generated Environment catalogue: {string.Join("; ", generatedErrors)}");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new Godot.Collections.Array<EnvironmentDecorationResource>();
        foreach (EnvironmentDecorationResource entry in Launch.Entries.Concat(Generated.Entries))
        {
            if (!GodotObject.IsInstanceValid(entry)) continue;
            if (!seen.Add(entry.DefinitionId))
                throw new InvalidOperationException($"Environment definition '{entry.DefinitionId}' appears in both trusted catalogue boundaries.");
            entries.Add(entry);
        }

        var composed = new EnvironmentDecorationCatalogueResource { Entries = entries };
        Godot.Collections.Array<string> errors = composed.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid composed Environment catalogue: {string.Join("; ", errors)}");
        return composed;
    }
}

using System;
using DesktopBuddy.Domain.Content;
using Godot;

namespace DesktopBuddy.Content;

/// <summary>
/// Loads the authored launch catalogue once per process and hands the composition roots a
/// validated domain snapshot. Static because the data is immutable authored content, not
/// per-run state: nothing here holds progress, ownership, or balance.
/// </summary>
public static class CatalogueLoader
{
    public const string CataloguePath = "res://data/catalogue/launch_catalogue.tres";

    private static CatalogueDefinition? _definition;
    private static ToolCatalogue? _catalogue;

    /// <summary>The authored definition, for the engine references the entries carry.</summary>
    public static CatalogueDefinition Definition =>
        _definition ??= GD.Load<CatalogueDefinition>(CataloguePath)
            ?? throw new InvalidOperationException(
                $"Missing or invalid catalogue resource at {CataloguePath}.");

    /// <summary>
    /// The validated FR-013 catalogue. Throws on malformed data: a build that cannot say
    /// what it sells must not start selling (ARCHITECTURE §16 fail-fast).
    /// </summary>
    public static ToolCatalogue Catalogue => _catalogue ??= Definition.ToCatalogue();
}

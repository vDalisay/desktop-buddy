using System;
using DesktopBuddy.Domain.Environment;
using Godot;

namespace DesktopBuddy.Environment;

public static class EnvironmentDecorationRegistry
{
    public const string CataloguePath = "res://data/environment/launch_decorations.tres";
    private static EnvironmentDecorationCatalogueResource? _catalogue;

    public static EnvironmentDecorationCatalogueResource Authored =>
        _catalogue ??= GD.Load<EnvironmentDecorationCatalogueResource>(CataloguePath)
            ?? throw new InvalidOperationException($"Missing Environment catalogue at {CataloguePath}.");

    public static DecorationCatalogue Domain => Authored.ToCatalogue();
    public static EnvironmentDecorationResource? Find(DecorationDefinitionId id) => Authored.Find(id);
}

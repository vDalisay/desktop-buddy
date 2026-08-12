using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>Immutable project-owned generated cosmetic registry loaded once from trusted res:// data.</summary>
public sealed class BuddyGeneratedCosmeticRegistry
{
    public const string CataloguePath = "res://data/cosmetics/generated/catalogue.tres";
    private static BuddyGeneratedCosmeticRegistry? _current;
    private readonly IReadOnlyDictionary<string, GeneratedBuddyCosmeticResource> _byFeatureId;

    private BuddyGeneratedCosmeticRegistry(GeneratedBuddyCosmeticCatalogueResource catalogue)
    {
        Godot.Collections.Array<string> errors = catalogue.Validate();
        if (errors.Count > 0) throw new InvalidOperationException($"Invalid generated cosmetic catalogue: {string.Join("; ", errors)}");
        var byId = new Dictionary<string, GeneratedBuddyCosmeticResource>(StringComparer.Ordinal);
        var definitions = new List<CosmeticDefinition>();
        foreach (string id in CharacterFeatureCatalog.Shipped.AllIds)
        {
            if (!CharacterFeatureCatalog.Shipped.TryGetDefinition(id, out CosmeticDefinition definition))
                throw new InvalidOperationException($"Shipped cosmetic '{id}' disappeared during catalogue composition.");
            definitions.Add(definition);
        }
        foreach (GeneratedBuddyCosmeticResource entry in catalogue.Entries)
        {
            if (CharacterFeatureCatalog.Shipped.TryGetDefinition(entry.FeatureId, out _))
                throw new InvalidOperationException($"Generated cosmetic '{entry.FeatureId}' collides with shipped content.");
            if (!byId.TryAdd(entry.FeatureId, entry))
                throw new InvalidOperationException($"Duplicate generated cosmetic '{entry.FeatureId}'.");
            definitions.Add(entry.ToDefinition());
        }
        _byFeatureId = new ReadOnlyDictionary<string, GeneratedBuddyCosmeticResource>(byId);
        FeatureCatalog = new CharacterFeatureCatalog(definitions);
        Entries = Array.AsReadOnly(catalogue.Entries.ToArray());
    }

    public static BuddyGeneratedCosmeticRegistry Current => _current ??= Load();
    public CharacterFeatureCatalog FeatureCatalog { get; }
    public IReadOnlyList<GeneratedBuddyCosmeticResource> Entries { get; }
    public bool TryGet(string featureId, out GeneratedBuddyCosmeticResource resource) => _byFeatureId.TryGetValue(featureId, out resource!);

    private static BuddyGeneratedCosmeticRegistry Load()
    {
        GeneratedBuddyCosmeticCatalogueResource catalogue = GD.Load<GeneratedBuddyCosmeticCatalogueResource>(CataloguePath)
            ?? throw new InvalidOperationException($"Missing generated cosmetic catalogue at {CataloguePath}.");
        return new BuddyGeneratedCosmeticRegistry(catalogue);
    }
}

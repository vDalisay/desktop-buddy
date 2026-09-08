using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// Trusted origin class for a compiled semantic definition. Package data never chooses this value;
/// the compiler/validator path that admitted the definition supplies it.
/// </summary>
public enum SemanticProviderKind
{
    Unknown = 0,
    Core = 1,
    LocalPack = 2,
    WorkshopPack = 3,
}

/// <summary>
/// Trusted identity of the provider that owns a definition namespace. Local and Workshop packs use
/// the same <c>ugc:&lt;pack-guid&gt;/...</c> semantic namespace; Workshop PublishedFileId remains separate
/// provenance and is intentionally not represented here.
/// </summary>
public readonly struct SemanticProviderIdentity : IEquatable<SemanticProviderIdentity>
{
    private SemanticProviderIdentity(SemanticProviderKind kind, Guid packId)
    {
        Kind = kind;
        PackId = packId;
    }

    public SemanticProviderKind Kind { get; }
    public Guid PackId { get; }
    public bool IsValid => Kind != SemanticProviderKind.Unknown;

    public static SemanticProviderIdentity Core =>
        new(SemanticProviderKind.Core, Guid.Empty);

    public static SemanticProviderIdentity LocalPack(Guid packId) =>
        CreatePack(SemanticProviderKind.LocalPack, packId);

    public static SemanticProviderIdentity WorkshopPack(Guid packId) =>
        CreatePack(SemanticProviderKind.WorkshopPack, packId);

    /// <summary>
    /// True only when the semantic namespace agrees with trusted provenance. This prevents a hostile
    /// package from claiming a <c>core:</c> ID or another pack's UGC namespace.
    /// </summary>
    public bool Owns(SemanticDefinitionId id)
    {
        if (!IsValid || !id.IsValid)
            return false;

        if (Kind == SemanticProviderKind.Core)
            return id.IsCore;

        return id.TryGetUgcPackId(out Guid idPack) && idPack == PackId;
    }

    public bool Equals(SemanticProviderIdentity other) => Kind == other.Kind && PackId == other.PackId;
    public override bool Equals(object? obj) => obj is SemanticProviderIdentity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Kind, PackId);
    public static bool operator ==(SemanticProviderIdentity left, SemanticProviderIdentity right) => left.Equals(right);
    public static bool operator !=(SemanticProviderIdentity left, SemanticProviderIdentity right) => !left.Equals(right);

    private static SemanticProviderIdentity CreatePack(SemanticProviderKind kind, Guid packId)
    {
        if (packId == Guid.Empty)
            throw new ArgumentException("Pack provider ID cannot be empty.", nameof(packId));
        return new SemanticProviderIdentity(kind, packId);
    }
}

/// <summary>Minimal contract shared by engine-free semantic definitions.</summary>
public interface ISemanticDefinition
{
    SemanticDefinitionId Id { get; }
}

/// <summary>A compiled definition paired with trusted provider identity.</summary>
public readonly record struct SemanticDefinitionRegistration<TDefinition>(
    TDefinition Definition,
    SemanticProviderIdentity Provider)
    where TDefinition : class, ISemanticDefinition;

/// <summary>
/// Immutable, deterministic registry for compiled semantic definitions. It owns identity uniqueness
/// and provider-namespace admission only; feature-specific validation belongs to each compiler.
/// </summary>
public sealed class SemanticDefinitionRegistry<TDefinition>
    where TDefinition : class, ISemanticDefinition
{
    private readonly IReadOnlyDictionary<SemanticDefinitionId, SemanticDefinitionRegistration<TDefinition>> _byId;

    public SemanticDefinitionRegistry(IEnumerable<SemanticDefinitionRegistration<TDefinition>> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var byId = new Dictionary<SemanticDefinitionId, SemanticDefinitionRegistration<TDefinition>>();
        var ordered = new List<SemanticDefinitionRegistration<TDefinition>>();

        foreach (SemanticDefinitionRegistration<TDefinition> registration in registrations)
        {
            if (registration.Definition is null)
                throw new InvalidOperationException("Semantic definition registration cannot contain a null definition.");
            if (!registration.Definition.Id.IsValid)
                throw new InvalidOperationException("Semantic definition registration contains an invalid ID.");
            if (!registration.Provider.IsValid)
                throw new InvalidOperationException($"Semantic definition '{registration.Definition.Id}' has no trusted provider identity.");
            if (!registration.Provider.Owns(registration.Definition.Id))
            {
                throw new InvalidOperationException(
                    $"Semantic definition '{registration.Definition.Id}' is outside provider '{registration.Provider.Kind}' namespace.");
            }
            if (!byId.TryAdd(registration.Definition.Id, registration))
                throw new InvalidOperationException($"Duplicate semantic definition ID '{registration.Definition.Id}'.");

            ordered.Add(registration);
        }

        _byId = new ReadOnlyDictionary<SemanticDefinitionId, SemanticDefinitionRegistration<TDefinition>>(byId);
        Entries = Array.AsReadOnly(ordered.ToArray());
    }

    public IReadOnlyList<SemanticDefinitionRegistration<TDefinition>> Entries { get; }
    public int Count => Entries.Count;

    public bool TryGet(SemanticDefinitionId id, out TDefinition definition)
    {
        if (_byId.TryGetValue(id, out SemanticDefinitionRegistration<TDefinition> registration))
        {
            definition = registration.Definition;
            return true;
        }

        definition = null!;
        return false;
    }

    public bool TryGetRegistration(
        SemanticDefinitionId id,
        out SemanticDefinitionRegistration<TDefinition> registration) =>
        _byId.TryGetValue(id, out registration);
}

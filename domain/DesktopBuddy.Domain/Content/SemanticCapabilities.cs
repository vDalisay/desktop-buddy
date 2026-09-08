using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Content;

/// <summary>
/// Maximum declarative trust surface authorized for a project-owned capability.
/// New capabilities default to <see cref="CoreOnly"/> until their parameters, hostile-input behavior
/// and performance envelope have been reviewed.
/// </summary>
public enum CapabilityExposure
{
    CoreOnly = 0,
    LocalDeclarative = 1,
    WorkshopDeclarative = 2,
}

public static class CapabilityExposurePolicy
{
    public static bool Allows(CapabilityExposure exposure, SemanticProviderKind providerKind) =>
        providerKind switch
        {
            SemanticProviderKind.Core => true,
            SemanticProviderKind.LocalPack =>
                exposure is CapabilityExposure.LocalDeclarative or CapabilityExposure.WorkshopDeclarative,
            SemanticProviderKind.WorkshopPack =>
                exposure == CapabilityExposure.WorkshopDeclarative,
            _ => false,
        };
}

/// <summary>
/// Trusted semantic capability descriptor. Capability implementations remain project-owned; external
/// packs can only request IDs exposed to their trust tier and supply parameters through later typed,
/// bounded schemas.
/// </summary>
public sealed class SemanticCapabilityDefinition : ISemanticDefinition
{
    private const string CapabilityPathPrefix = "capability/";

    public SemanticCapabilityDefinition(
        SemanticDefinitionId id,
        CapabilityExposure exposure = CapabilityExposure.CoreOnly)
    {
        if (!id.IsCore || !id.Path.StartsWith(CapabilityPathPrefix, StringComparison.Ordinal) ||
            id.Path.Length == CapabilityPathPrefix.Length)
        {
            throw new ArgumentException(
                "Capability IDs must use the project-owned core:capability/<name> namespace.",
                nameof(id));
        }
        if (!Enum.IsDefined(exposure))
            throw new ArgumentOutOfRangeException(nameof(exposure), exposure, "Unknown capability exposure level.");

        Id = id;
        Exposure = exposure;
    }

    public SemanticDefinitionId Id { get; }
    public CapabilityExposure Exposure { get; }
}

/// <summary>
/// Immutable project-owned capability registry. Unknown capabilities fail closed. A pack's trusted
/// provider identity, not package-authored metadata, decides which exposure tier is applicable.
/// </summary>
public sealed class SemanticCapabilityRegistry
{
    private readonly SemanticDefinitionRegistry<SemanticCapabilityDefinition> _definitions;

    public SemanticCapabilityRegistry(IEnumerable<SemanticCapabilityDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var registrations = new List<SemanticDefinitionRegistration<SemanticCapabilityDefinition>>();
        foreach (SemanticCapabilityDefinition definition in definitions)
        {
            registrations.Add(new SemanticDefinitionRegistration<SemanticCapabilityDefinition>(
                definition,
                SemanticProviderIdentity.Core));
        }

        _definitions = new SemanticDefinitionRegistry<SemanticCapabilityDefinition>(registrations);
    }

    public IReadOnlyList<SemanticDefinitionRegistration<SemanticCapabilityDefinition>> Entries =>
        _definitions.Entries;

    public bool TryGet(SemanticDefinitionId id, out SemanticCapabilityDefinition definition) =>
        _definitions.TryGet(id, out definition);

    public bool CanUse(SemanticDefinitionId id, SemanticProviderIdentity consumer)
    {
        if (!consumer.IsValid || !_definitions.TryGet(id, out SemanticCapabilityDefinition definition))
            return false;

        return CapabilityExposurePolicy.Allows(definition.Exposure, consumer.Kind);
    }
}

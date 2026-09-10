using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Content;

namespace DesktopBuddy.Domain.Sandbox;

/// <summary>
/// The buildable parts this build ships. They are core semantic definitions, so they go through the
/// same trusted registry a local or Workshop pack would have to, and a placement can only ever name
/// a definition the registry admitted.
/// </summary>
public static class SandboxPartCatalogue
{
    public static SemanticDefinitionId WoodBeam { get; } = SemanticDefinitionId.CreateCore("part/wood_beam");
    public static SemanticDefinitionId MetalBlock { get; } = SemanticDefinitionId.CreateCore("part/metal_block");
    public static SemanticDefinitionId MetalPlate { get; } = SemanticDefinitionId.CreateCore("part/metal_plate");
    public static SemanticDefinitionId Wheel { get; } = SemanticDefinitionId.CreateCore("part/wheel");

    /// <summary>Definitions in palette order.</summary>
    public static IReadOnlyList<SandboxPartDefinition> Definitions { get; } =
    [
        new(WoodBeam, "Wood Beam", SandboxPartShape.Box, SandboxPartMaterial.Wood,
            Width: 96.0f, Height: 16.0f, Mass: 6.0f, Bounce: 0.05f, Friction: 0.9f),
        new(MetalBlock, "Metal Block", SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 32.0f, Height: 32.0f, Mass: 12.0f, Bounce: 0.02f, Friction: 0.7f),
        new(MetalPlate, "Metal Plate", SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 128.0f, Height: 8.0f, Mass: 9.0f, Bounce: 0.02f, Friction: 0.7f),
        new(Wheel, "Wheel", SandboxPartShape.Circle, SandboxPartMaterial.Rubber,
            Width: 40.0f, Height: 40.0f, Mass: 3.0f, Bounce: 0.15f, Friction: 1.4f),
    ];

    public static SemanticDefinitionRegistry<SandboxPartDefinition> Registry { get; } = CreateRegistry();

    public static bool TryGet(SemanticDefinitionId id, out SandboxPartDefinition definition) =>
        Registry.TryGet(id, out definition);

    private static SemanticDefinitionRegistry<SandboxPartDefinition> CreateRegistry()
    {
        foreach (SandboxPartDefinition definition in Definitions)
        {
            IReadOnlyList<string> problems = definition.Validate();
            if (problems.Count > 0)
                throw new InvalidOperationException($"Shipped part '{definition.Id}' is invalid: {string.Join("; ", problems)}");
        }

        return new SemanticDefinitionRegistry<SandboxPartDefinition>(
            Definitions.Select(definition =>
                new SemanticDefinitionRegistration<SandboxPartDefinition>(definition, SemanticProviderIdentity.Core)));
    }
}

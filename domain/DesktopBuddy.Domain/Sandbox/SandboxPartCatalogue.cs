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
    public static SemanticDefinitionId Button { get; } = SemanticDefinitionId.CreateCore("part/button");
    public static SemanticDefinitionId Timer { get; } = SemanticDefinitionId.CreateCore("part/timer");
    public static SemanticDefinitionId Piston { get; } = SemanticDefinitionId.CreateCore("part/piston");
    public static SemanticDefinitionId WeaponTrigger { get; } = SemanticDefinitionId.CreateCore("part/weapon_trigger");
    public static SemanticDefinitionId Lamp { get; } = SemanticDefinitionId.CreateCore("part/lamp");

    /// <summary>One definition for every tool lying in a room; its overrides say which tool.</summary>
    public static SemanticDefinitionId Tool { get; } = SemanticDefinitionId.CreateCore("part/tool");

    /// <summary>Definitions in palette order.</summary>
    public static IReadOnlyList<SandboxPartDefinition> Definitions { get; } =
    [
        new(WoodBeam, "Wood Beam",
            "A long plank. Light enough to shove, long enough to bridge a gap or make a ramp.",
            SandboxPartShape.Box, SandboxPartMaterial.Wood,
            Width: 96.0f, Height: 16.0f, Mass: 6.0f, Bounce: 0.05f, Friction: 0.9f),
        new(MetalBlock, "Metal Block",
            "A heavy cube. Barely moves when hit, so it makes a solid base or a stubborn wall.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 32.0f, Height: 32.0f, Mass: 12.0f, Bounce: 0.02f, Friction: 0.7f),
        new(MetalPlate, "Metal Plate",
            "A wide, thin sheet. The flattest thing you can stand on, and the best platform top.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 128.0f, Height: 8.0f, Mass: 9.0f, Bounce: 0.02f, Friction: 0.7f),
        new(Wheel, "Wheel",
            "A grippy rubber disc. It rolls, so put two under a beam and you have a cart.",
            SandboxPartShape.Circle, SandboxPartMaterial.Rubber,
            Width: 40.0f, Height: 40.0f, Mass: 3.0f, Bounce: 0.15f, Friction: 1.4f),
        // NF-4 devices: the Next Fest signal vocabulary.
        new(Button, "Button",
            "Click it while the room plays and it sends a pulse down its wire.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 32.0f, Height: 16.0f, Mass: 4.0f, Bounce: 0.02f, Friction: 0.8f, SandboxDeviceKind.Button),
        new(Timer, "Timer",
            "A clock: sends a pulse every few seconds. Wire something into it and each pulse switches it on or off.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 32.0f, Height: 32.0f, Mass: 4.0f, Bounce: 0.02f, Friction: 0.8f, SandboxDeviceKind.Timer),
        new(Piston, "Piston",
            "Shoves out hard when a pulse comes in, then pulls back.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 32.0f, Height: 32.0f, Mass: 8.0f, Bounce: 0.02f, Friction: 0.8f, SandboxDeviceKind.Piston),
        new(WeaponTrigger, "Weapon Trigger",
            "Holds a gun and pulls its trigger every time a pulse comes in.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 48.0f, Height: 24.0f, Mass: 5.0f, Bounce: 0.02f, Friction: 0.8f, SandboxDeviceKind.WeaponTrigger),
        new(Lamp, "Lamp",
            "Lights up with one pulse and goes out with the next.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 24.0f, Height: 32.0f, Mass: 2.0f, Bounce: 0.02f, Friction: 0.8f, SandboxDeviceKind.Lamp),
        // A tool lying in the room. The size and weight here are only a fallback: a placement takes
        // the shape, the weight and the look of the tool it actually holds.
        new(Tool, "Tool",
            "A tool lying in the room. Rope it, weld it or hinge it to anything, and pick it up to use it.",
            SandboxPartShape.Box, SandboxPartMaterial.Metal,
            Width: 32.0f, Height: 32.0f, Mass: 3.0f, Bounce: 0.1f, Friction: 0.9f),
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

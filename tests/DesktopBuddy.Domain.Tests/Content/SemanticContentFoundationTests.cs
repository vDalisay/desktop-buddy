using System;
using DesktopBuddy.Domain.Content;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Content;

public sealed class SemanticContentFoundationTests
{
    private static readonly Guid PackA = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid PackB = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void Core_definition_id_round_trips_as_canonical_semantic_identity()
    {
        SemanticDefinitionId id = SemanticDefinitionId.Parse("core:construction/wood_beam");

        Assert.True(id.IsValid);
        Assert.True(id.IsCore);
        Assert.False(id.IsUgc);
        Assert.Equal("core", id.Provider);
        Assert.Equal("construction/wood_beam", id.Path);
        Assert.Equal("core:construction/wood_beam", id.Value);
        Assert.Equal(id, SemanticDefinitionId.CreateCore("construction/wood_beam"));
    }

    [Fact]
    public void Ugc_definition_id_contains_stable_pack_namespace_without_Steam_identity()
    {
        SemanticDefinitionId id = SemanticDefinitionId.CreateUgc(PackA, "entity/my_launcher");

        Assert.True(id.IsUgc);
        Assert.Equal($"ugc:{PackA:N}/entity/my_launcher", id.Value);
        Assert.True(id.TryGetUgcPackId(out Guid parsedPack));
        Assert.Equal(PackA, parsedPack);
    }

    [Theory]
    [InlineData("")]
    [InlineData("tool.pistol")]
    [InlineData("core:")]
    [InlineData("core:Construction/wood_beam")]
    [InlineData("core:construction/wood.beam")]
    [InlineData("core:construction/../wood_beam")]
    [InlineData("core:construction\\wood_beam")]
    [InlineData("core:/construction/wood_beam")]
    [InlineData("core:construction//wood_beam")]
    [InlineData("unknown:construction/wood_beam")]
    [InlineData("next_fest:entity/button")]
    [InlineData("ugc:entity/my_launcher")]
    [InlineData("ugc:00000000000000000000000000000000/entity/my_launcher")]
    [InlineData(" core:device/button")]
    [InlineData("core:device/button ")]
    public void Noncanonical_or_filesystem_shaped_ids_are_rejected(string value)
    {
        Assert.False(SemanticDefinitionId.TryParse(value, out SemanticDefinitionId id));
        Assert.False(id.IsValid);
    }

    [Fact]
    public void Legacy_shipped_content_ids_remain_legacy_and_are_not_silently_rekeyed()
    {
        Assert.True(ContentIds.IsKnown(ContentIds.ToolPistol));
        Assert.False(SemanticDefinitionId.TryParse(ContentIds.ToolPistol, out _));
        Assert.False(SemanticDefinitionId.TryParse(ContentIds.CosmeticWorkGlasses, out _));
    }

    [Fact]
    public void Provider_identity_prevents_core_spoofing_and_cross_pack_claims()
    {
        SemanticDefinitionId core = SemanticDefinitionId.Parse("core:device/button");
        SemanticDefinitionId packA = SemanticDefinitionId.CreateUgc(PackA, "entity/button");
        SemanticDefinitionId packB = SemanticDefinitionId.CreateUgc(PackB, "entity/button");
        SemanticProviderIdentity localA = SemanticProviderIdentity.LocalPack(PackA);
        SemanticProviderIdentity workshopA = SemanticProviderIdentity.WorkshopPack(PackA);

        Assert.True(SemanticProviderIdentity.Core.Owns(core));
        Assert.False(SemanticProviderIdentity.Core.Owns(packA));
        Assert.True(localA.Owns(packA));
        Assert.True(workshopA.Owns(packA));
        Assert.False(localA.Owns(core));
        Assert.False(localA.Owns(packB));
    }

    [Fact]
    public void Empty_pack_identity_is_never_admitted()
    {
        Assert.Throws<ArgumentException>(() => SemanticProviderIdentity.LocalPack(Guid.Empty));
        Assert.Throws<ArgumentException>(() => SemanticProviderIdentity.WorkshopPack(Guid.Empty));
        Assert.Throws<ArgumentException>(() => SemanticDefinitionId.CreateUgc(Guid.Empty, "entity/item"));
    }

    [Fact]
    public void Registry_is_deterministic_and_resolves_definition_with_trusted_provider()
    {
        var core = new TestDefinition(SemanticDefinitionId.Parse("core:construction/wood_beam"));
        var ugc = new TestDefinition(SemanticDefinitionId.CreateUgc(PackA, "entity/my_beam"));
        var registry = new SemanticDefinitionRegistry<TestDefinition>(new[]
        {
            new SemanticDefinitionRegistration<TestDefinition>(core, SemanticProviderIdentity.Core),
            new SemanticDefinitionRegistration<TestDefinition>(ugc, SemanticProviderIdentity.WorkshopPack(PackA)),
        });

        Assert.Equal(2, registry.Count);
        Assert.Equal(core.Id, registry.Entries[0].Definition.Id);
        Assert.Equal(ugc.Id, registry.Entries[1].Definition.Id);
        Assert.True(registry.TryGet(core.Id, out TestDefinition coreResolved));
        Assert.Same(core, coreResolved);
        Assert.True(registry.TryGetRegistration(ugc.Id, out SemanticDefinitionRegistration<TestDefinition> registration));
        Assert.Equal(SemanticProviderKind.WorkshopPack, registration.Provider.Kind);
        Assert.Equal(PackA, registration.Provider.PackId);
    }

    [Fact]
    public void Registry_rejects_duplicate_ids_even_when_origins_differ()
    {
        SemanticDefinitionId id = SemanticDefinitionId.CreateUgc(PackA, "entity/shared");
        var first = new TestDefinition(id);
        var second = new TestDefinition(id);

        Assert.Throws<InvalidOperationException>(() => new SemanticDefinitionRegistry<TestDefinition>(new[]
        {
            new SemanticDefinitionRegistration<TestDefinition>(first, SemanticProviderIdentity.LocalPack(PackA)),
            new SemanticDefinitionRegistration<TestDefinition>(second, SemanticProviderIdentity.WorkshopPack(PackA)),
        }));
    }

    [Fact]
    public void Registry_rejects_definition_outside_trusted_provider_namespace()
    {
        var coreSpoof = new TestDefinition(SemanticDefinitionId.Parse("core:device/fake_button"));
        var crossPack = new TestDefinition(SemanticDefinitionId.CreateUgc(PackB, "entity/item"));

        Assert.Throws<InvalidOperationException>(() => new SemanticDefinitionRegistry<TestDefinition>(new[]
        {
            new SemanticDefinitionRegistration<TestDefinition>(coreSpoof, SemanticProviderIdentity.WorkshopPack(PackA)),
        }));
        Assert.Throws<InvalidOperationException>(() => new SemanticDefinitionRegistry<TestDefinition>(new[]
        {
            new SemanticDefinitionRegistration<TestDefinition>(crossPack, SemanticProviderIdentity.LocalPack(PackA)),
        }));
    }

    [Fact]
    public void Capability_defaults_to_core_only_and_unknown_capability_fails_closed()
    {
        SemanticDefinitionId physicsId = SemanticDefinitionId.Parse("core:capability/physics_body");
        var registry = new SemanticCapabilityRegistry(new[]
        {
            new SemanticCapabilityDefinition(physicsId),
        });

        Assert.True(registry.TryGet(physicsId, out SemanticCapabilityDefinition definition));
        Assert.Equal(CapabilityExposure.CoreOnly, definition.Exposure);
        Assert.True(registry.CanUse(physicsId, SemanticProviderIdentity.Core));
        Assert.False(registry.CanUse(physicsId, SemanticProviderIdentity.LocalPack(PackA)));
        Assert.False(registry.CanUse(physicsId, SemanticProviderIdentity.WorkshopPack(PackA)));
        Assert.False(registry.CanUse(
            SemanticDefinitionId.Parse("core:capability/not_registered"),
            SemanticProviderIdentity.Core));
    }

    [Fact]
    public void Capability_exposure_is_monotonic_across_trust_tiers()
    {
        SemanticDefinitionId localId = SemanticDefinitionId.Parse("core:capability/local_preview");
        SemanticDefinitionId workshopId = SemanticDefinitionId.Parse("core:capability/painted_visual");
        var registry = new SemanticCapabilityRegistry(new[]
        {
            new SemanticCapabilityDefinition(localId, CapabilityExposure.LocalDeclarative),
            new SemanticCapabilityDefinition(workshopId, CapabilityExposure.WorkshopDeclarative),
        });
        SemanticProviderIdentity local = SemanticProviderIdentity.LocalPack(PackA);
        SemanticProviderIdentity workshop = SemanticProviderIdentity.WorkshopPack(PackA);

        Assert.True(registry.CanUse(localId, SemanticProviderIdentity.Core));
        Assert.True(registry.CanUse(localId, local));
        Assert.False(registry.CanUse(localId, workshop));

        Assert.True(registry.CanUse(workshopId, SemanticProviderIdentity.Core));
        Assert.True(registry.CanUse(workshopId, local));
        Assert.True(registry.CanUse(workshopId, workshop));
    }

    [Fact]
    public void Capability_ids_are_always_project_owned_semantic_capabilities()
    {
        Assert.Throws<ArgumentException>(() => new SemanticCapabilityDefinition(
            SemanticDefinitionId.Parse("core:device/button")));
        Assert.Throws<ArgumentException>(() => new SemanticCapabilityDefinition(
            SemanticDefinitionId.CreateUgc(PackA, "capability/physics_body")));
    }

    private sealed record TestDefinition(SemanticDefinitionId Id) : ISemanticDefinition;
}

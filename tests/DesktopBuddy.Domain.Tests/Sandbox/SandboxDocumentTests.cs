using System;
using System.Linq;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Sandbox;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sandbox;

public sealed class SandboxDocumentTests
{
    private static CanonicalRoomPosition At(float x, float y) => new(x, y);

    [Fact]
    public void BeamsTakeTheirOwnSizeAndWeighWhatTheyMeasure()
    {
        SandboxPartCatalogue.TryGet(SandboxPartCatalogue.WoodBeam, out SandboxPartDefinition beam);
        SandboxPartCatalogue.TryGet(SandboxPartCatalogue.Piston, out SandboxPartDefinition piston);

        SandboxPartDefinition longer = new SandboxPartOverrides(Length: beam.Width * 2.0f, Thickness: beam.Height * 1.5f)
            .SizedDefinition(beam);
        Assert.Equal(beam.Width * 2.0f, longer.Width);
        Assert.Equal(beam.Height * 1.5f, longer.Height);
        Assert.Equal(beam.Mass * 3.0f, longer.Mass, 3);
        Assert.Equal(beam.Id, longer.Id);

        // Bounded, never trusted; devices and wheels keep their shape; no setting is the shared part.
        SandboxPartDefinition wild = new SandboxPartOverrides(Length: 1e9f, Thickness: -3.0f).SizedDefinition(beam);
        Assert.Equal(SandboxPartOverrides.MaximumLength, wild.Width);
        Assert.Equal(SandboxPartOverrides.MinimumThickness, wild.Height);
        Assert.Same(piston, new SandboxPartOverrides(Length: 200.0f).SizedDefinition(piston));
        Assert.Same(beam, SandboxPartOverrides.None.SizedDefinition(beam));

        var document = new SandboxDocument();
        SandboxPartId id = document.Add(SandboxPartCatalogue.WoodBeam, At(0.5f, 0.5f),
            overrides: new SandboxPartOverrides(Length: 200.0f, Thickness: 24.0f)).Part!.PartId;
        SandboxDocument reloaded = SandboxSavePolicy.Decode(SandboxSavePolicy.Serialize(document)).Document!;
        Assert.True(reloaded.TryGet(id, out PlacedSandboxPart? saved));
        Assert.Equal(200.0f, saved!.Overrides.Length);
        Assert.Equal(24.0f, saved.Overrides.Thickness);
    }

    [Fact]
    public void ShippedPartsAreValidCoreDefinitions()
    {
        Assert.Equal(11, SandboxPartCatalogue.Definitions.Count);
        foreach (SandboxPartDefinition definition in SandboxPartCatalogue.Definitions)
        {
            Assert.Empty(definition.Validate());
            Assert.True(definition.Id.IsCore);
            Assert.True(SandboxPartCatalogue.TryGet(definition.Id, out SandboxPartDefinition resolved));
            Assert.Same(definition, resolved);
        }
    }

    /// <summary>Every mount names the gun it holds, and nothing else does.</summary>
    [Fact]
    public void OnlyWeaponMountsNameAGun()
    {
        foreach (SandboxPartDefinition definition in SandboxPartCatalogue.Definitions)
        {
            Assert.Equal(
                definition.Device == SandboxDeviceKind.WeaponTrigger,
                SandboxPartCatalogue.WeaponOf(definition.Id) is not null);
        }
        Assert.Equal(Domain.Tools.ToolId.Shotgun, SandboxPartCatalogue.WeaponOf(SandboxPartCatalogue.ShotgunTrigger));
    }

    [Fact]
    public void AddingPartsAssignsDistinctIdsAndBumpsRevision()
    {
        var document = new SandboxDocument();
        Assert.Equal(0, document.Revision);

        SandboxEditResult first = document.Add(SandboxPartCatalogue.WoodBeam, At(0.25f, 0.5f));
        SandboxEditResult second = document.Add(SandboxPartCatalogue.Wheel, At(0.75f, 0.5f));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Part!.PartId, second.Part!.PartId);
        Assert.Equal(2, document.Count);
        Assert.Equal(2, document.Revision);
    }

    [Fact]
    public void RemovingAPartLeavesTheRestAndReportsMissingOnes()
    {
        var document = new SandboxDocument();
        SandboxPartId kept = document.Add(SandboxPartCatalogue.WoodBeam, At(0.5f, 0.5f)).Part!.PartId;
        SandboxPartId removed = document.Add(SandboxPartCatalogue.MetalBlock, At(0.5f, 0.4f)).Part!.PartId;

        Assert.True(document.Remove(removed).Succeeded);
        Assert.Equal(SandboxEditStatus.PartNotFound, document.Remove(removed).Status);
        Assert.Single(document.Parts);
        Assert.True(document.TryGet(kept, out _));
    }

    [Fact]
    public void MovingToTheSamePlaceIsNoChange()
    {
        var document = new SandboxDocument();
        SandboxPartId partId = document.Add(SandboxPartCatalogue.WoodBeam, At(0.5f, 0.5f), 90.0f).Part!.PartId;
        long revision = document.Revision;

        Assert.Equal(SandboxEditStatus.NoChange, document.Move(partId, At(0.5f, 0.5f), 90.0f).Status);
        Assert.Equal(revision, document.Revision);

        Assert.True(document.Move(partId, At(0.6f, 0.5f), 450.0f).Succeeded);
        Assert.True(document.TryGet(partId, out PlacedSandboxPart? moved));
        Assert.Equal(90.0f, moved!.RotationDegrees);
        Assert.Equal(0.6f, moved.Position.X);
    }

    [Fact]
    public void OverridesAreClampedAndNeverTouchTheSharedDefinition()
    {
        var document = new SandboxDocument();
        SandboxPartId partId = document.Add(SandboxPartCatalogue.MetalBlock, At(0.5f, 0.5f)).Part!.PartId;
        Assert.True(SandboxPartCatalogue.TryGet(SandboxPartCatalogue.MetalBlock, out SandboxPartDefinition definition));
        float canonicalMass = definition.Mass;

        Assert.True(document.SetOverrides(partId, new SandboxPartOverrides(MassScale: 1000.0f, Bounce: 5.0f, GravityScale: -99.0f, Frozen: true)).Succeeded);
        Assert.True(document.TryGet(partId, out PlacedSandboxPart? tuned));

        Assert.Equal(SandboxPartOverrides.MaximumMassScale, tuned!.Overrides.MassScale);
        Assert.Equal(1.0f, tuned.Overrides.Bounce);
        Assert.Equal(SandboxPartOverrides.MinimumGravityScale, tuned.Overrides.GravityScale);
        Assert.True(tuned.Overrides.Frozen);
        Assert.Equal(canonicalMass * SandboxPartOverrides.MaximumMassScale, tuned.Overrides.MassFor(definition));
        Assert.Equal(canonicalMass, definition.Mass);

        // Clearing an override restores the canonical value rather than writing one back.
        Assert.True(document.SetOverrides(partId, SandboxPartOverrides.None).Succeeded);
        Assert.True(document.TryGet(partId, out PlacedSandboxPart? restored));
        Assert.False(restored!.Overrides.HasAny);
        Assert.Equal(canonicalMass, restored.Overrides.MassFor(definition));
        Assert.Equal(definition.Bounce, restored.Overrides.BounceFor(definition));
    }

    [Fact]
    public void TheRoomIsBounded()
    {
        var document = new SandboxDocument();
        for (int index = 0; index < SandboxDocument.MaximumParts; index++)
            Assert.True(document.Add(SandboxPartCatalogue.WoodBeam, At(0.5f, 0.5f)).Succeeded);

        Assert.False(document.CanAdd);
        Assert.Equal(SandboxEditStatus.LimitReached, document.Add(SandboxPartCatalogue.Wheel, At(0.5f, 0.5f)).Status);
    }

    [Fact]
    public void DuplicatingARoomKeepsItsPartsButNotTheirIdentities()
    {
        var document = new SandboxDocument();
        document.Add(SandboxPartCatalogue.WoodBeam, At(0.25f, 0.5f), 45.0f);
        document.Add(SandboxPartCatalogue.Wheel, At(0.75f, 0.5f));

        SandboxDocument copy = document.CopyWithNewPartIds();

        Assert.Equal(document.Count, copy.Count);
        Assert.Empty(copy.Parts.Select(part => part.PartId).Intersect(document.Parts.Select(part => part.PartId)));
        Assert.Equal(
            document.Parts.Select(part => (part.DefinitionId, part.Position.X, part.RotationDegrees)),
            copy.Parts.Select(part => (part.DefinitionId, part.Position.X, part.RotationDegrees)));
    }

    [Fact]
    public void DocumentsRoundTripThroughTheSavePolicy()
    {
        var document = new SandboxDocument();
        SandboxPartId beam = document.Add(SandboxPartCatalogue.WoodBeam, At(0.3f, 0.6f), 30.0f).Part!.PartId;
        document.Add(SandboxPartCatalogue.Wheel, At(0.7f, 0.8f));
        document.SetOverrides(beam, new SandboxPartOverrides(MassScale: 2.0f, Frozen: true));

        SandboxDocumentDecodeResult decoded = SandboxSavePolicy.Decode(SandboxSavePolicy.Serialize(document));

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        SandboxDocument restored = decoded.Document!;
        Assert.Equal(document.Revision, restored.Revision);
        Assert.Equal(
            document.Parts.Select(part => (part.PartId, part.DefinitionId, part.RotationDegrees, part.Overrides)),
            restored.Parts.Select(part => (part.PartId, part.DefinitionId, part.RotationDegrees, part.Overrides)));
    }

    [Fact]
    public void UnknownPartsAreDroppedRatherThanFailingTheWholeRoom()
    {
        string json = """
        {
          "schemaVersion": 1,
          "revision": 4,
          "parts": [
            { "partId": "11111111-1111-4111-8111-111111111111", "definitionId": "core:part/wood_beam",
              "canonicalX": 0.5, "canonicalY": 0.5, "rotationDegrees": 0 },
            { "partId": "22222222-2222-4222-8222-222222222222", "definitionId": "core:part/anti_gravity_engine",
              "canonicalX": 0.5, "canonicalY": 0.5, "rotationDegrees": 0 }
          ]
        }
        """;

        SandboxDocumentDecodeResult decoded = SandboxSavePolicy.Decode(json);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Single(decoded.Document!.Parts);
        Assert.Equal(SandboxPartCatalogue.WoodBeam, decoded.Document!.Parts[0].DefinitionId);
    }

    [Fact]
    public void MalformedAndFutureDocumentsAreRefusedRatherThanGuessed()
    {
        Assert.Equal(SaveDecodeStatus.Malformed, SandboxSavePolicy.Decode("{ not json").Status);
        Assert.Equal(SaveDecodeStatus.Malformed, SandboxSavePolicy.Decode("{}").Status);
        Assert.Equal(
            SaveDecodeStatus.UnsupportedFutureVersion,
            SandboxSavePolicy.Decode("""{ "schemaVersion": 99, "parts": [] }""").Status);
    }

    [Fact]
    public void DuplicatePartIdsAreRejected()
    {
        Guid duplicate = Guid.Parse("33333333-3333-4333-8333-333333333333");
        var part = new PlacedSandboxPart(
            SandboxPartId.From(duplicate),
            SandboxPartCatalogue.WoodBeam,
            At(0.5f, 0.5f),
            0.0f,
            SandboxPartOverrides.None);

        Assert.Throws<ArgumentException>(() => new SandboxDocument([part, part]));
    }
}

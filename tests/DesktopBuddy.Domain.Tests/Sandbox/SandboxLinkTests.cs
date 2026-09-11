using System.Linq;
using System.Text.Json.Nodes;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Sandbox;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Sandbox;

public sealed class SandboxLinkTests
{
    private static (SandboxDocument Document, SandboxPartId Beam, SandboxPartId Wheel) Cart()
    {
        var document = new SandboxDocument();
        SandboxPartId beam = document.Add(SandboxPartCatalogue.WoodBeam, new CanonicalRoomPosition(0.5f, 0.5f)).Part!.PartId;
        SandboxPartId wheel = document.Add(SandboxPartCatalogue.Wheel, new CanonicalRoomPosition(0.6f, 0.55f)).Part!.PartId;
        return (document, beam, wheel);
    }

    [Fact]
    public void Link_tuning_and_wire_colour_are_bounded_and_survive_a_save()
    {
        (SandboxDocument document, SandboxPartId beam, SandboxPartId wheel) = Cart();
        SandboxLink rope = document.AddLink(SandboxLinkKind.Rope, SandboxLinkEnd.OnPart(beam, 0, 0),
            SandboxLinkEnd.World(0.5f, 0.1f), 120.0f, strength: 0.25f, elasticity: 0.9f, stiffness: 0.7f).Link!;
        Assert.Equal(0.25f, rope.Strength);
        Assert.Equal(0.9f, rope.Elasticity);
        Assert.Equal(0.0f, rope.Stiffness);            // stiffness only means something on a hinge
        Assert.False(rope.IsUnbreakable);

        SandboxLink hinge = document.AddLink(SandboxLinkKind.Hinge, SandboxLinkEnd.OnPart(beam, 40, 0),
            SandboxLinkEnd.OnPart(wheel, 0, 0), strength: 7.0f, stiffness: float.NaN).Link!;
        Assert.True(hinge.IsUnbreakable);              // clamped, not trusted
        Assert.Equal(0.0f, hinge.Stiffness);

        long revision = document.Revision;
        Assert.True(document.SetLinkTuning(hinge.LinkId, 0.3f, 0.0f, 0.6f).Succeeded);
        Assert.Equal(revision + 1, document.Revision);
        Assert.Equal(SandboxLinkStatus.LinkNotFound, document.SetLinkTuning(SandboxLinkId.New(), 1, 0, 0).Status);

        var devices = new SandboxDocument();
        SandboxPartId button = devices.Add(SandboxPartCatalogue.Button, new CanonicalRoomPosition(0.1f, 0.5f)).Part!.PartId;
        SandboxPartId lamp = devices.Add(SandboxPartCatalogue.Lamp, new CanonicalRoomPosition(0.2f, 0.5f)).Part!.PartId;
        Assert.Equal(SandboxWireColor.Purple,
            devices.AddWire(button, SandboxDevices.Out, lamp, SandboxDevices.In, SandboxWireColor.Purple).Wire!.Color);

        SandboxDocument reloaded = SandboxSavePolicy.Decode(SandboxSavePolicy.Serialize(document)).Document!;
        Assert.Equal(document.Links, reloaded.Links);
        Assert.Equal(SandboxWireColor.Purple,
            Assert.Single(SandboxSavePolicy.Decode(SandboxSavePolicy.Serialize(devices)).Document!.Wires).Color);

        // A room from before tuning: links come back unbreakable, taut and free.
        JsonObject save = JsonNode.Parse(SandboxSavePolicy.Serialize(document))!.AsObject();
        foreach (JsonNode? link in save["links"]!.AsArray())
        {
            link!.AsObject().Remove("strength");
            link.AsObject().Remove("elasticity");
            link.AsObject().Remove("stiffness");
        }
        Assert.All(SandboxSavePolicy.Decode(save.ToJsonString()).Document!.Links, link =>
        {
            Assert.True(link.IsUnbreakable);
            Assert.Equal(0.0f, link.Elasticity);
            Assert.Equal(0.0f, link.Stiffness);
        });

        Assert.All(SandboxLinkPresets.All, preset =>
            Assert.Null(new SandboxLink(SandboxLinkId.New(), preset.Kind, SandboxLinkEnd.OnPart(beam, 0, 0),
                    preset.Kind == SandboxLinkKind.Weld ? SandboxLinkEnd.OnPart(wheel, 0, 0) : SandboxLinkEnd.World(0.5f, 0.5f),
                    preset.Kind == SandboxLinkKind.Rope ? 50.0f : 0.0f)
                .WithTuning(preset.Strength, preset.Elasticity, preset.Stiffness).Problem()));
    }

    [Fact]
    public void Valid_links_are_stored_and_invalid_ones_leave_nothing_behind()
    {
        (SandboxDocument document, SandboxPartId beam, SandboxPartId wheel) = Cart();
        long revision = document.Revision;

        SandboxLinkResult hinge = document.AddLink(
            SandboxLinkKind.Hinge, SandboxLinkEnd.OnPart(beam, 40, 0), SandboxLinkEnd.OnPart(wheel, 0, 0));
        Assert.True(hinge.Succeeded);
        Assert.Equal(revision + 1, document.Revision);

        // Same kind between the same two parts, in the other order, is the same link.
        Assert.Equal(SandboxLinkStatus.Duplicate, document.AddLink(
            SandboxLinkKind.Hinge, SandboxLinkEnd.OnPart(wheel, 0, 0), SandboxLinkEnd.OnPart(beam, 40, 0)).Status);
        Assert.Equal(SandboxLinkStatus.Invalid, document.AddLink(
            SandboxLinkKind.Hinge, SandboxLinkEnd.OnPart(beam, 0, 0), SandboxLinkEnd.OnPart(beam, 10, 0)).Status);
        Assert.Equal(SandboxLinkStatus.Invalid, document.AddLink(
            SandboxLinkKind.Weld, SandboxLinkEnd.OnPart(beam, 0, 0), SandboxLinkEnd.World(0.5f, 0.1f)).Status);
        Assert.Equal(SandboxLinkStatus.Invalid, document.AddLink(
            SandboxLinkKind.Rope, SandboxLinkEnd.OnPart(beam, 0, 0), SandboxLinkEnd.World(1.5f, 0.1f)).Status);
        Assert.Equal(SandboxLinkStatus.PartNotFound, document.AddLink(
            SandboxLinkKind.Weld, SandboxLinkEnd.OnPart(beam, 0, 0), SandboxLinkEnd.OnPart(SandboxPartId.New(), 0, 0)).Status);

        Assert.Single(document.Links);
        Assert.Equal(revision + 1, document.Revision);
    }

    [Fact]
    public void Rope_length_is_clamped_into_its_band()
    {
        (SandboxDocument document, SandboxPartId beam, _) = Cart();
        SandboxLink shortRope = document.AddLink(
            SandboxLinkKind.Rope, SandboxLinkEnd.OnPart(beam, 0, 0), SandboxLinkEnd.World(0.5f, 0.0f), 1.0f).Link!;
        SandboxLink longRope = document.AddLink(
            SandboxLinkKind.Rope, SandboxLinkEnd.OnPart(beam, 10, 0), SandboxLinkEnd.World(0.4f, 0.0f), 1e9f).Link!;

        Assert.Equal(SandboxLink.MinimumRopeLength, shortRope.Length);
        Assert.Equal(SandboxLink.MaximumRopeLength, longRope.Length);
    }

    [Fact]
    public void Removing_a_part_removes_every_link_touching_it()
    {
        (SandboxDocument document, SandboxPartId beam, SandboxPartId wheel) = Cart();
        document.AddLink(SandboxLinkKind.Hinge, SandboxLinkEnd.OnPart(beam, 40, 0), SandboxLinkEnd.OnPart(wheel, 0, 0));
        document.AddLink(SandboxLinkKind.Rope, SandboxLinkEnd.OnPart(beam, 0, 0), SandboxLinkEnd.World(0.5f, 0.0f), 100.0f);
        document.AddLink(SandboxLinkKind.Rope, SandboxLinkEnd.OnPart(wheel, 0, 0), SandboxLinkEnd.World(0.7f, 0.0f), 100.0f);

        document.Remove(beam);

        SandboxLink survivor = Assert.Single(document.Links);
        Assert.Equal(wheel, survivor.A.PartId);
        Assert.True(survivor.B.IsWorld);
    }

    [Fact]
    public void Links_round_trip_and_older_or_damaged_saves_still_open()
    {
        (SandboxDocument document, SandboxPartId beam, SandboxPartId wheel) = Cart();
        document.AddLink(SandboxLinkKind.Weld, SandboxLinkEnd.OnPart(beam, -30, 0), SandboxLinkEnd.OnPart(wheel, 5, -5));
        document.AddLink(SandboxLinkKind.Rope, SandboxLinkEnd.OnPart(beam, 0, 0), SandboxLinkEnd.World(0.25f, 0.05f), 120.0f);

        SandboxDocumentDecodeResult decoded = SandboxSavePolicy.Decode(SandboxSavePolicy.Serialize(document));
        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        Assert.Equal(document.Links, decoded.Document!.Links);

        // A version-1 room predates links and opens with none.
        JsonObject older = JsonNode.Parse(SandboxSavePolicy.Serialize(document))!.AsObject();
        older["schemaVersion"] = 1;
        older.Remove("links");
        SandboxDocumentDecodeResult legacy = SandboxSavePolicy.Decode(older.ToJsonString());
        Assert.Equal(SaveDecodeStatus.Valid, legacy.Status);
        Assert.Equal(2, legacy.Document!.Count);
        Assert.Empty(legacy.Document.Links);

        // A link of an unknown kind, or to a part that is not there, is dropped, not fatal.
        JsonObject damaged = JsonNode.Parse(SandboxSavePolicy.Serialize(document))!.AsObject();
        JsonArray links = damaged["links"]!.AsArray();
        links[0]!["kind"] = "motor";
        links[1]!["aPartId"] = SandboxPartId.New().Value.ToString();
        SandboxDocumentDecodeResult repaired = SandboxSavePolicy.Decode(damaged.ToJsonString());
        Assert.Equal(SaveDecodeStatus.Valid, repaired.Status);
        Assert.Equal(2, repaired.Document!.Count);
        Assert.Empty(repaired.Document.Links);
    }

    [Fact]
    public void Snapshot_keeps_parts_links_and_revision_and_does_not_follow_later_edits()
    {
        (SandboxDocument document, SandboxPartId beam, SandboxPartId wheel) = Cart();
        document.AddLink(SandboxLinkKind.Hinge, SandboxLinkEnd.OnPart(beam, 40, 0), SandboxLinkEnd.OnPart(wheel, 0, 0));

        SandboxDocument snapshot = document.Snapshot();
        document.Remove(wheel);

        Assert.Equal(2, snapshot.Count);
        Assert.Single(snapshot.Links);
        Assert.Empty(document.Links);
        Assert.NotEqual(document.Revision, snapshot.Revision);
    }

    [Fact]
    public void Scene_duplication_remaps_links_onto_the_copied_parts()
    {
        (SandboxDocument document, SandboxPartId beam, SandboxPartId wheel) = Cart();
        document.AddLink(SandboxLinkKind.Hinge, SandboxLinkEnd.OnPart(beam, 40, 0), SandboxLinkEnd.OnPart(wheel, 0, 0));

        SandboxDocument copy = document.CopyWithNewPartIds();

        SandboxLink link = Assert.Single(copy.Links);
        Assert.NotEqual(document.Links[0].LinkId, link.LinkId);
        Assert.Contains(copy.Parts, part => part.PartId == link.A.PartId);
        Assert.Contains(copy.Parts, part => part.PartId == link.B.PartId);
        Assert.DoesNotContain(document.Parts, part => part.PartId == link.A.PartId);
    }
}

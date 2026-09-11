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

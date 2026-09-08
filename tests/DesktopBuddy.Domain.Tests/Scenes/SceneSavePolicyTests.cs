using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class SceneSavePolicyTests
{
    [Fact]
    public void Scene_round_trip_preserves_ids_environment_and_buddy_anchors()
    {
        SceneId sceneId = SceneId.From(Id(1));
        BuddyIdentityId buddyId = BuddyIdentityId.From(Id(2));
        BuddyPlacementId placementId = BuddyPlacementId.From(Id(3));
        var decoration = new PlacedDecoration(
            new PlacedDecorationId(Id(4)),
            new DecorationDefinitionId("decoration.lamp.test"),
            new CanonicalRoomPosition(0.2f, 0.8f),
            RotationDegrees: 90,
            DecorationRenderBand.BehindBuddyFloor,
            PurchasePriceMilliCredits: 5_000);
        var source = new SceneDocument(
            sceneId,
            "Home",
            new EnvironmentLayout([decoration]),
            [new BuddyPlacement(placementId, buddyId, new CanonicalRoomPosition(0.45f, 0.7f))]);

        string json = SceneSavePolicy.SerializeScene(source);
        SceneDocumentDecodeResult decoded = SceneSavePolicy.DecodeScene(json);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        SceneDocument restored = Assert.IsType<SceneDocument>(decoded.Scene);
        Assert.Equal(sceneId, restored.SceneId);
        Assert.Equal("Home", restored.Name);
        Assert.Single(restored.Environment.Decorations);
        Assert.Equal(decoration, restored.Environment.Decorations[0]);
        Assert.Single(restored.BuddyPlacements);
        Assert.Equal(placementId, restored.BuddyPlacements[0].PlacementId);
        Assert.Equal(buddyId, restored.BuddyPlacements[0].BuddyIdentityId);
        Assert.Equal(0.45f, restored.BuddyPlacements[0].Position.X);
        Assert.Equal(0.7f, restored.BuddyPlacements[0].Position.Y);
    }

    [Fact]
    public void Scene_decode_rejects_duplicate_buddy_identity_and_bad_anchor()
    {
        string duplicateBuddy = $$"""
        {
          "schemaVersion": 1,
          "sceneId": "{{Id(1)}}",
          "name": "Home",
          "environmentSchemaVersion": {{EnvironmentLayout.CurrentSchemaVersion}},
          "decorations": [],
          "buddyPlacements": [
            { "placementId": "{{Id(2)}}", "buddyIdentityId": "{{Id(10)}}", "canonicalX": 0.2, "canonicalY": 0.7 },
            { "placementId": "{{Id(3)}}", "buddyIdentityId": "{{Id(10)}}", "canonicalX": 0.8, "canonicalY": 0.7 }
          ]
        }
        """;
        string badAnchor = $$"""
        {
          "schemaVersion": 1,
          "sceneId": "{{Id(1)}}",
          "name": "Home",
          "environmentSchemaVersion": {{EnvironmentLayout.CurrentSchemaVersion}},
          "decorations": [],
          "buddyPlacements": [
            { "placementId": "{{Id(2)}}", "buddyIdentityId": "{{Id(10)}}", "canonicalX": 1.5, "canonicalY": 0.7 }
          ]
        }
        """;

        Assert.Equal(SaveDecodeStatus.Invalid, SceneSavePolicy.DecodeScene(duplicateBuddy).Status);
        Assert.Equal(SaveDecodeStatus.Invalid, SceneSavePolicy.DecodeScene(badAnchor).Status);
    }

    [Fact]
    public void Scene_decode_rejects_invalid_environment_entry_before_runtime()
    {
        string json = $$"""
        {
          "schemaVersion": 1,
          "sceneId": "{{Id(1)}}",
          "name": "Home",
          "environmentSchemaVersion": {{EnvironmentLayout.CurrentSchemaVersion}},
          "decorations": [
            {
              "instanceId": "{{Id(4)}}",
              "definitionId": "not-a-decoration-id",
              "canonicalX": 0.4,
              "canonicalY": 0.7,
              "rotationDegrees": 0,
              "renderBand": 3,
              "purchasePriceMilliCredits": 1000
            }
          ],
          "buddyPlacements": []
        }
        """;

        SceneDocumentDecodeResult decoded = SceneSavePolicy.DecodeScene(json);

        Assert.Equal(SaveDecodeStatus.Invalid, decoded.Status);
        Assert.Null(decoded.Scene);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Scene_decode_reports_malformed_payloads(string json)
    {
        Assert.Equal(SaveDecodeStatus.Malformed, SceneSavePolicy.DecodeScene(json).Status);
    }

    [Fact]
    public void Future_scene_schema_is_preserved_as_unsupported_not_corrupt()
    {
        string json = "{\"schemaVersion\":999}";

        SceneDocumentDecodeResult result = SceneSavePolicy.DecodeScene(json);

        Assert.Equal(SaveDecodeStatus.UnsupportedFutureVersion, result.Status);
        Assert.Null(result.Scene);
    }

    [Fact]
    public void Index_round_trip_preserves_order_active_identity_and_revision()
    {
        SceneId first = SceneId.From(Id(1));
        SceneId second = SceneId.From(Id(2));
        var firstScene = new SceneDocument(first, "Home", new EnvironmentLayout());
        var secondScene = new SceneDocument(second, "Lab", new EnvironmentLayout());
        var library = new SceneLibraryState(NextFest(), [firstScene, secondScene], second);
        SceneIndexSave index = SceneSavePolicy.CreateIndex(library, revision: 17);

        string json = SceneSavePolicy.SerializeIndex(index);
        SceneIndexDecodeResult decoded = SceneSavePolicy.DecodeIndex(json);

        Assert.Equal(SaveDecodeStatus.Valid, decoded.Status);
        SceneIndexSave restored = Assert.IsType<SceneIndexSave>(decoded.Index);
        Assert.Equal(17, restored.Revision);
        Assert.Equal(second.Value, restored.ActiveSceneId);
        Assert.Equal(new[] { first.Value, second.Value }, restored.OrderedSceneIds);
    }

    [Fact]
    public void Index_rejects_missing_active_duplicate_ids_and_future_schema()
    {
        string missingActive = $$"""
        {
          "schemaVersion": 1,
          "revision": 0,
          "activeSceneId": "{{Id(9)}}",
          "orderedSceneIds": ["{{Id(1)}}", "{{Id(2)}}"]
        }
        """;
        string duplicate = $$"""
        {
          "schemaVersion": 1,
          "revision": 0,
          "activeSceneId": "{{Id(1)}}",
          "orderedSceneIds": ["{{Id(1)}}", "{{Id(1)}}"]
        }
        """;

        Assert.Equal(SaveDecodeStatus.Invalid, SceneSavePolicy.DecodeIndex(missingActive).Status);
        Assert.Equal(SaveDecodeStatus.Invalid, SceneSavePolicy.DecodeIndex(duplicate).Status);
        Assert.Equal(
            SaveDecodeStatus.UnsupportedFutureVersion,
            SceneSavePolicy.DecodeIndex("{\"schemaVersion\":2}").Status);
    }

    [Fact]
    public void Index_creation_requires_a_nonempty_committed_library()
    {
        var empty = new SceneLibraryState(NextFest());
        Assert.Throws<InvalidOperationException>(() => SceneSavePolicy.CreateIndex(empty, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SceneSavePolicy.CreateIndex(
                new SceneLibraryState(
                    NextFest(),
                    [new SceneDocument(SceneId.From(Id(1)), "Home", new EnvironmentLayout())],
                    SceneId.From(Id(1))),
                -1));
    }

    private static BuildScopePolicy NextFest() => BuildScopePolicy.Resolve(
        itchIo: false,
        steamDemo: true,
        nextFestDemo: true,
        fullRelease: false);

    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:D12}");
}

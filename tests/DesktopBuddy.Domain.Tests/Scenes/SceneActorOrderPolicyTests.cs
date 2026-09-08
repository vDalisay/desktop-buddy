using System;
using System.IO;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Scenes;

public sealed class SceneActorOrderPolicyTests
{
    [Fact]
    public void Live_actor_bindings_are_resolved_in_scene_document_order_not_registration_order()
    {
        BuddyIdentityId a = BuddyIdentityId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        BuddyIdentityId b = BuddyIdentityId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        BuddyIdentityId c = BuddyIdentityId.From(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        BuddyPlacementId pa = BuddyPlacementId.From(Guid.Parse("10000000-0000-0000-0000-000000000001"));
        BuddyPlacementId pb = BuddyPlacementId.From(Guid.Parse("10000000-0000-0000-0000-000000000002"));
        BuddyPlacementId pc = BuddyPlacementId.From(Guid.Parse("10000000-0000-0000-0000-000000000003"));
        SceneDocument scene = Scene(pa, a, pb, b, pc, c);

        var registrationOrder = new[]
        {
            new SceneActorBindingKey(pc, c),
            new SceneActorBindingKey(pa, a),
            new SceneActorBindingKey(pb, b),
        };

        var ordered = SceneActorOrderPolicy.Resolve(scene, registrationOrder);

        Assert.Equal(pa, ordered[0].PlacementId);
        Assert.Equal(a, ordered[0].BuddyIdentityId);
        Assert.Equal(pb, ordered[1].PlacementId);
        Assert.Equal(b, ordered[1].BuddyIdentityId);
        Assert.Equal(pc, ordered[2].PlacementId);
        Assert.Equal(c, ordered[2].BuddyIdentityId);
    }

    [Fact]
    public void Missing_extra_duplicate_or_cross_bound_actor_bindings_fail_closed()
    {
        BuddyIdentityId a = BuddyIdentityId.From(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        BuddyIdentityId b = BuddyIdentityId.From(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"));
        BuddyIdentityId c = BuddyIdentityId.From(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        BuddyPlacementId pa = BuddyPlacementId.From(Guid.Parse("20000000-0000-0000-0000-000000000001"));
        BuddyPlacementId pb = BuddyPlacementId.From(Guid.Parse("20000000-0000-0000-0000-000000000002"));
        SceneDocument scene = Scene(pa, a, pb, b);

        Assert.Throws<ArgumentException>(() => SceneActorOrderPolicy.Resolve(
            scene,
            [new SceneActorBindingKey(pa, a)]));
        Assert.Throws<ArgumentException>(() => SceneActorOrderPolicy.Resolve(
            scene,
            [
                new SceneActorBindingKey(pa, a),
                new SceneActorBindingKey(pb, b),
                new SceneActorBindingKey(BuddyPlacementId.New(), c),
            ]));
        Assert.Throws<ArgumentException>(() => SceneActorOrderPolicy.Resolve(
            scene,
            [
                new SceneActorBindingKey(pa, a),
                new SceneActorBindingKey(pa, b),
            ]));
        Assert.Throws<ArgumentException>(() => SceneActorOrderPolicy.Resolve(
            scene,
            [
                new SceneActorBindingKey(pa, b),
                new SceneActorBindingKey(pb, a),
            ]));
    }

    [Fact]
    public void Runtime_actor_host_files_cannot_introduce_a_second_gameplay_physics_callback()
    {
        string? root = FindRepositoryRoot(AppContext.BaseDirectory);
        Assert.NotNull(root);

        string actor = File.ReadAllText(Path.Combine(root!, "src", "Scenes", "BuddyActorRuntime.cs"));
        string host = File.ReadAllText(Path.Combine(root!, "src", "Scenes", "SceneRuntimeHost.cs"));

        Assert.DoesNotContain("_PhysicsProcess(", actor, StringComparison.Ordinal);
        Assert.DoesNotContain("_PhysicsProcess(", host, StringComparison.Ordinal);
        Assert.DoesNotContain("SceneRuntimeHost : Node", host, StringComparison.Ordinal);
    }

    private static SceneDocument Scene(params object[] pairs)
    {
        if (pairs.Length % 2 != 0)
            throw new ArgumentException("Expected placement/Buddy pairs.", nameof(pairs));

        var placements = new BuddyPlacement[pairs.Length / 2];
        for (int index = 0; index < placements.Length; index++)
        {
            placements[index] = new BuddyPlacement(
                (BuddyPlacementId)pairs[index * 2],
                (BuddyIdentityId)pairs[(index * 2) + 1],
                new CanonicalRoomPosition(0.2f + (index * 0.2f), 0.7f));
        }

        return new SceneDocument(SceneId.New(), "Order Test", new EnvironmentLayout(), placements);
    }

    private static string? FindRepositoryRoot(string start)
    {
        DirectoryInfo? current = new(start);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "project.godot")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }
}

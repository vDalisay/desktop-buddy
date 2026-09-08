using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Scenes;

/// <summary>Engine-free identity key supplied by one live Buddy actor binding.</summary>
public readonly record struct SceneActorBindingKey(
    BuddyPlacementId PlacementId,
    DesktopBuddy.Domain.Persistence.BuddyIdentityId BuddyIdentityId);

/// <summary>
/// Resolves live actor bindings into the exact stable order authored by a <see cref="SceneDocument"/>.
/// Runtime Scene-tree discovery order must never decide multi-Buddy simulation order.
/// </summary>
public static class SceneActorOrderPolicy
{
    public static IReadOnlyList<SceneActorBindingKey> Resolve(
        SceneDocument scene,
        IEnumerable<SceneActorBindingKey> liveActors)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(liveActors);

        var byPlacement = new Dictionary<BuddyPlacementId, SceneActorBindingKey>();
        var buddyIds = new HashSet<DesktopBuddy.Domain.Persistence.BuddyIdentityId>();
        foreach (SceneActorBindingKey actor in liveActors)
        {
            if (!actor.PlacementId.IsValid || !actor.BuddyIdentityId.IsValid)
                throw new ArgumentException("Live Scene actors require stable placement and Buddy IDs.", nameof(liveActors));
            if (!byPlacement.TryAdd(actor.PlacementId, actor))
                throw new ArgumentException("Live Scene actors contain a duplicate placement binding.", nameof(liveActors));
            if (!buddyIds.Add(actor.BuddyIdentityId))
                throw new ArgumentException("One Buddy identity cannot have two live actors in one Scene.", nameof(liveActors));
        }

        if (byPlacement.Count != scene.BuddyPlacements.Count)
            throw new ArgumentException("Live Scene actor count does not match the Scene document roster.", nameof(liveActors));

        var ordered = new SceneActorBindingKey[scene.BuddyPlacements.Count];
        for (int index = 0; index < scene.BuddyPlacements.Count; index++)
        {
            BuddyPlacement placement = scene.BuddyPlacements[index];
            if (!byPlacement.TryGetValue(placement.PlacementId, out SceneActorBindingKey actor))
                throw new ArgumentException($"Scene placement '{placement.PlacementId}' has no live Buddy actor.", nameof(liveActors));
            if (actor.BuddyIdentityId != placement.BuddyIdentityId)
            {
                throw new ArgumentException(
                    $"Live actor for placement '{placement.PlacementId}' is bound to the wrong Buddy identity.",
                    nameof(liveActors));
            }
            ordered[index] = actor;
        }

        return ordered;
    }
}

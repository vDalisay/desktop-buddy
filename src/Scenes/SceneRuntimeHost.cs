using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Scenes;

namespace DesktopBuddy.Scenes;

/// <summary>
/// Ordered runtime view of one active Scene's Buddy cast. This is intentionally a plain managed
/// object, not a Node: SandboxRoot remains the one authoritative gameplay <c>_PhysicsProcess</c>.
/// Inactive Scenes have no SceneRuntimeHost and therefore no hidden Buddy simulation.
/// </summary>
public sealed class SceneRuntimeHost
{
    private readonly BuddyActorRuntime[] _actors;

    public SceneRuntimeHost(SceneDocument scene, IEnumerable<BuddyActorRuntime> liveActors)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentNullException.ThrowIfNull(liveActors);

        var byPlacement = new Dictionary<BuddyPlacementId, BuddyActorRuntime>();
        var keys = new List<SceneActorBindingKey>();
        foreach (BuddyActorRuntime actor in liveActors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!byPlacement.TryAdd(actor.PlacementId, actor))
                throw new ArgumentException("Live Scene actors contain a duplicate placement binding.", nameof(liveActors));
            keys.Add(actor.BindingKey);
        }

        IReadOnlyList<SceneActorBindingKey> orderedKeys = SceneActorOrderPolicy.Resolve(scene, keys);
        _actors = new BuddyActorRuntime[orderedKeys.Count];
        for (int index = 0; index < orderedKeys.Count; index++)
            _actors[index] = byPlacement[orderedKeys[index].PlacementId];
    }

    public SceneDocument Scene { get; }
    public IReadOnlyList<BuddyActorRuntime> Actors => _actors;

    /// <summary>Samples every active Buddy in stable Scene-document order.</summary>
    public void CaptureTickSnapshots()
    {
        for (int index = 0; index < _actors.Length; index++)
            _actors[index].CaptureTickSnapshot();
    }

    /// <summary>
    /// Routes one fixed-tick slice across every active Buddy in stable Scene order. The caller owns
    /// shared input/tool phases and supplies each actor's targeted grab/rope context.
    /// </summary>
    public void PhysicsTick(Func<BuddyActorRuntime, BuddyActorTickContext> contextForActor)
    {
        ArgumentNullException.ThrowIfNull(contextForActor);
        for (int index = 0; index < _actors.Length; index++)
        {
            BuddyActorRuntime actor = _actors[index];
            BuddyActorTickContext context = contextForActor(actor);
            actor.PhysicsTick(context);
        }
    }

    /// <summary>
    /// Resolves the owner of one physical Buddy part without using UI focus or scene-tree parent
    /// discovery. This is the seam shared grab/tools will use when SandboxRoot is migrated.
    /// </summary>
    public bool TryResolveActor(PuppetPartBody? part, out BuddyActorRuntime actor)
    {
        for (int index = 0; index < _actors.Length; index++)
        {
            if (_actors[index].OwnsPart(part))
            {
                actor = _actors[index];
                return true;
            }
        }

        actor = null!;
        return false;
    }
}

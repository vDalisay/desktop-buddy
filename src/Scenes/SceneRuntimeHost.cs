using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Persistence;
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
    private readonly SceneProgressBindingRegistry? _progressBindings;

    /// <summary>
    /// Legacy-compatible host constructor. It validates Scene/actor identity and order but does not
    /// claim split progress has been composed. Kept for the Initial Demo path and existing tests.
    /// </summary>
    public SceneRuntimeHost(SceneDocument scene, IEnumerable<BuddyActorRuntime> liveActors)
        : this(scene, liveActors, progressBindings: null)
    {
    }

    /// <summary>
    /// Multi-Buddy constructor. Every live actor must resolve to the exact placement/Buddy pair in
    /// the supplied progress registry, which in turn guarantees one shared player ledger and one
    /// actor-local Buddy state per placement before the host can tick anything.
    /// </summary>
    public SceneRuntimeHost(
        SceneProgressBindingRegistry progressBindings,
        IEnumerable<BuddyActorRuntime> liveActors)
        : this(
            (progressBindings ?? throw new ArgumentNullException(nameof(progressBindings))).Scene,
            liveActors,
            progressBindings)
    {
    }

    private SceneRuntimeHost(
        SceneDocument scene,
        IEnumerable<BuddyActorRuntime> liveActors,
        SceneProgressBindingRegistry? progressBindings)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentNullException.ThrowIfNull(liveActors);
        if (progressBindings is not null && !ReferenceEquals(progressBindings.Scene, scene))
        {
            throw new ArgumentException(
                "Scene runtime host and progress registry must reference the same Scene document.",
                nameof(progressBindings));
        }

        var byPlacement = new Dictionary<BuddyPlacementId, BuddyActorRuntime>();
        var keys = new List<SceneActorBindingKey>();
        foreach (BuddyActorRuntime actor in liveActors)
        {
            ArgumentNullException.ThrowIfNull(actor);
            if (!byPlacement.TryAdd(actor.PlacementId, actor))
                throw new ArgumentException("Live Scene actors contain a duplicate placement binding.", nameof(liveActors));
            keys.Add(actor.BindingKey);

            if (progressBindings is not null)
            {
                SceneBuddyProgressBinding progress = progressBindings.ForPlacement(actor.PlacementId);
                if (progress.Placement.BuddyIdentityId != actor.BuddyIdentityId)
                {
                    throw new ArgumentException(
                        "Live Buddy actor does not match the Buddy identity bound to its Scene placement.",
                        nameof(liveActors));
                }
            }
        }

        IReadOnlyList<SceneActorBindingKey> orderedKeys = SceneActorOrderPolicy.Resolve(scene, keys);
        _actors = new BuddyActorRuntime[orderedKeys.Count];
        for (int index = 0; index < orderedKeys.Count; index++)
            _actors[index] = byPlacement[orderedKeys[index].PlacementId];

        if (progressBindings is not null && progressBindings.Count != _actors.Length)
        {
            throw new ArgumentException(
                "Scene progress roster and live actor roster must have the same size.",
                nameof(progressBindings));
        }

        _progressBindings = progressBindings;
    }

    public SceneDocument Scene { get; }
    public IReadOnlyList<BuddyActorRuntime> Actors => _actors;
    public bool UsesSplitProgress => _progressBindings is not null;
    public SceneProgressBindingRegistry? ProgressBindings => _progressBindings;

    /// <summary>
    /// Returns the actor-local progress seam for a split-state host. There is deliberately no
    /// fallback to another Buddy or to legacy progress when the actor does not belong to this host.
    /// </summary>
    public BuddyRuntimeProgressBinding ProgressFor(BuddyActorRuntime actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        SceneProgressBindingRegistry progressBindings = _progressBindings
            ?? throw new InvalidOperationException(
                "This SceneRuntimeHost was composed through the legacy progress path.");
        SceneBuddyProgressBinding binding = progressBindings.ForPlacement(actor.PlacementId);
        if (binding.Placement.BuddyIdentityId != actor.BuddyIdentityId)
        {
            throw new InvalidOperationException(
                "Live Buddy actor no longer matches the progress binding for its placement.");
        }
        return binding.Progress;
    }

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

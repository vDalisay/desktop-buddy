using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Domain.Scenes;

/// <summary>
/// One validated progress binding for one Buddy placement in the active Scene. Placement identity
/// is Scene-owned, Buddy identity is persistent across Scenes, and the coordinator binds that Buddy
/// to the one shared account state.
/// </summary>
public readonly record struct SceneBuddyProgressBinding(
    BuddyPlacement Placement,
    BuddyProgressCoordinator Coordinator,
    BuddyRuntimeProgressBinding Progress);

/// <summary>
/// Engine-free composition boundary for the active Scene's progress graph. It proves before any
/// Godot actor is instantiated that every placement resolves exactly one persistent Buddy identity,
/// every Buddy identity is used exactly once in this Scene, and every coordinator shares the same
/// <see cref="PlayerProgressState"/>.
///
/// The registry deliberately contains no fallback/default Buddy. A malformed or partially loaded
/// Scene must fail composition instead of accidentally binding two live actors to the same emotional
/// state or creating a second wallet.
/// </summary>
public sealed class SceneProgressBindingRegistry
{
    private readonly SceneBuddyProgressBinding[] _ordered;
    private readonly Dictionary<BuddyPlacementId, int> _byPlacement = new();
    private readonly Dictionary<BuddyIdentityId, int> _byBuddy = new();

    public SceneProgressBindingRegistry(
        PlayerProgressState player,
        SceneDocument scene,
        IEnumerable<BuddyIdentityState> buddyIdentities)
    {
        Player = player ?? throw new ArgumentNullException(nameof(player));
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        ArgumentNullException.ThrowIfNull(buddyIdentities);

        var identities = new Dictionary<BuddyIdentityId, BuddyIdentityState>();
        foreach (BuddyIdentityState buddy in buddyIdentities)
        {
            ArgumentNullException.ThrowIfNull(buddy);
            if (!identities.TryAdd(buddy.BuddyIdentityId, buddy))
            {
                throw new ArgumentException(
                    "Scene progress composition contains the same Buddy identity more than once.",
                    nameof(buddyIdentities));
            }
        }

        if (identities.Count != scene.BuddyPlacements.Count)
        {
            throw new ArgumentException(
                "Scene progress composition must provide exactly the Buddy identities placed in the Scene.",
                nameof(buddyIdentities));
        }

        _ordered = new SceneBuddyProgressBinding[scene.BuddyPlacements.Count];
        for (int index = 0; index < scene.BuddyPlacements.Count; index++)
        {
            BuddyPlacement placement = scene.BuddyPlacements[index];
            if (!identities.TryGetValue(placement.BuddyIdentityId, out BuddyIdentityState? buddy))
            {
                throw new ArgumentException(
                    $"Scene placement {placement.PlacementId} references Buddy {placement.BuddyIdentityId}, but that identity was not loaded.",
                    nameof(buddyIdentities));
            }

            var coordinator = new BuddyProgressCoordinator(player, buddy);
            var binding = new BuddyRuntimeProgressBinding(coordinator);
            _ordered[index] = new SceneBuddyProgressBinding(placement, coordinator, binding);
            _byPlacement.Add(placement.PlacementId, index);
            _byBuddy.Add(placement.BuddyIdentityId, index);
        }
    }

    public PlayerProgressState Player { get; }
    public SceneDocument Scene { get; }
    public int Count => _ordered.Length;
    public IReadOnlyList<SceneBuddyProgressBinding> OrderedBindings => _ordered;

    public SceneBuddyProgressBinding ForPlacement(BuddyPlacementId placementId)
    {
        if (!_byPlacement.TryGetValue(placementId, out int index))
            throw new KeyNotFoundException($"Scene has no Buddy placement {placementId}.");
        return _ordered[index];
    }

    public SceneBuddyProgressBinding ForBuddy(BuddyIdentityId buddyIdentityId)
    {
        if (!_byBuddy.TryGetValue(buddyIdentityId, out int index))
            throw new KeyNotFoundException($"Scene has no Buddy identity {buddyIdentityId}.");
        return _ordered[index];
    }

    public bool TryForPlacement(
        BuddyPlacementId placementId,
        out SceneBuddyProgressBinding binding)
    {
        if (_byPlacement.TryGetValue(placementId, out int index))
        {
            binding = _ordered[index];
            return true;
        }

        binding = default;
        return false;
    }
}

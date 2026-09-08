using System;
using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;

namespace DesktopBuddy.Domain.Scenes;

public enum SceneLibraryStatus
{
    Succeeded = 0,
    ScenesUnavailable,
    LimitReached,
    SceneNotFound,
    BuddyNotFound,
    BuddyAlreadyPresent,
    CannotDeleteLastScene,
    NoChange,
}

public readonly record struct SceneLibraryResult(
    SceneLibraryStatus Status,
    SceneDocument? Scene = null)
{
    public bool Succeeded => Status == SceneLibraryStatus.Succeeded;
}

/// <summary>
/// Engine-free owner of the ordered local Scene library. It is deliberately a document state
/// machine rather than a physics/runtime owner: inactive Scenes are persisted documents and never
/// advance simulation. The active Scene ID is a stable identity, never a tab index or display name.
/// </summary>
public sealed class SceneLibraryState
{
    private readonly BuildScopePolicy _scope;
    private readonly Func<SceneId> _newSceneId;
    private readonly Func<BuddyPlacementId> _newPlacementId;
    private readonly List<SceneDocument> _scenes;

    public SceneLibraryState(
        BuildScopePolicy scope,
        IEnumerable<SceneDocument>? scenes = null,
        SceneId activeSceneId = default,
        Func<SceneId>? sceneIdFactory = null,
        Func<BuddyPlacementId>? placementIdFactory = null)
    {
        _scope = scope;
        _newSceneId = sceneIdFactory ?? SceneId.New;
        _newPlacementId = placementIdFactory ?? BuddyPlacementId.New;
        _scenes = scenes?.ToList() ?? [];

        var ids = new HashSet<SceneId>();
        foreach (SceneDocument scene in _scenes)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (!ids.Add(scene.SceneId))
                throw new ArgumentException("Scene library cannot contain duplicate Scene IDs.", nameof(scenes));
        }

        if (_scope.MaximumSceneCount is int limit && _scenes.Count > limit)
            throw new ArgumentException("Scene library exceeds the active build's Scene cap.", nameof(scenes));

        if (_scenes.Count == 0)
        {
            if (activeSceneId.IsValid)
                throw new ArgumentException("An empty Scene library cannot name an active Scene.", nameof(activeSceneId));
            ActiveSceneId = default;
            return;
        }

        if (!activeSceneId.IsValid || !ids.Contains(activeSceneId))
            throw new ArgumentException("Active Scene ID must identify a document in the library.", nameof(activeSceneId));
        ActiveSceneId = activeSceneId;
    }

    public IReadOnlyList<SceneDocument> Scenes => _scenes;
    public SceneId ActiveSceneId { get; private set; }
    public SceneDocument? ActiveScene => TryGet(ActiveSceneId, out SceneDocument? scene) ? scene : null;
    public int Count => _scenes.Count;
    public bool CanCreate => _scope.CanCreateScene(_scenes.Count);

    public bool TryGet(SceneId sceneId, out SceneDocument? scene)
    {
        int index = IndexOf(sceneId);
        scene = index >= 0 ? _scenes[index] : null;
        return scene is not null;
    }

    public SceneLibraryResult Create(string name, EnvironmentLayout? environment = null) =>
        Create(name, new EnvironmentProgressSnapshot(0, environment ?? new EnvironmentLayout(), []));

    public SceneLibraryResult Create(string name, EnvironmentProgressSnapshot environment)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        if (!_scope.CanCreateScene(_scenes.Count))
            return new SceneLibraryResult(SceneLibraryStatus.LimitReached);

        SceneId id = NextUniqueSceneId();
        var scene = new SceneDocument(id, name, environment);
        _scenes.Add(scene);
        if (!ActiveSceneId.IsValid)
            ActiveSceneId = id;
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, scene);
    }

    public SceneLibraryResult Switch(SceneId sceneId)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        if (!TryGet(sceneId, out SceneDocument? scene))
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);
        if (sceneId == ActiveSceneId)
            return new SceneLibraryResult(SceneLibraryStatus.NoChange, scene);
        ActiveSceneId = sceneId;
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, scene);
    }

    public SceneLibraryResult Rename(SceneId sceneId, string newName)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        int index = IndexOf(sceneId);
        if (index < 0)
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);

        SceneDocument current = _scenes[index];
        SceneDocument.ValidateName(newName);
        if (string.Equals(current.Name, newName, StringComparison.Ordinal))
            return new SceneLibraryResult(SceneLibraryStatus.NoChange, current);

        SceneDocument renamed = CopyScene(current, current.SceneId, newName, preservePlacementIds: true);
        _scenes[index] = renamed;
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, renamed);
    }

    public SceneLibraryResult Duplicate(SceneId sourceSceneId, string? newName = null)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        if (!_scope.CanCreateScene(_scenes.Count))
            return new SceneLibraryResult(SceneLibraryStatus.LimitReached);
        int sourceIndex = IndexOf(sourceSceneId);
        if (sourceIndex < 0)
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);

        SceneDocument source = _scenes[sourceIndex];
        string name = newName ?? DuplicateName(source.Name);
        SceneDocument.ValidateName(name);
        SceneDocument duplicate = CopyScene(
            source,
            NextUniqueSceneId(),
            name,
            preservePlacementIds: false);
        _scenes.Insert(sourceIndex + 1, duplicate);
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, duplicate);
    }

    public SceneLibraryResult Delete(SceneId sceneId)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        int index = IndexOf(sceneId);
        if (index < 0)
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);
        if (_scenes.Count == 1)
            return new SceneLibraryResult(SceneLibraryStatus.CannotDeleteLastScene, _scenes[0]);

        SceneDocument removed = _scenes[index];
        bool removedActive = removed.SceneId == ActiveSceneId;
        _scenes.RemoveAt(index);
        if (removedActive)
        {
            int replacementIndex = Math.Min(index, _scenes.Count - 1);
            ActiveSceneId = _scenes[replacementIndex].SceneId;
        }
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, removed);
    }

    /// <summary>
    /// Replaces the complete Environment progress owned by one Scene without changing its identity,
    /// name or Buddy roster. Revision tracking remains the coordinator's responsibility so this same
    /// primitive can restore the exact prior document after a failed atomic save.
    /// </summary>
    public SceneLibraryResult UpdateEnvironment(SceneId sceneId, EnvironmentProgressSnapshot environment)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        int index = IndexOf(sceneId);
        if (index < 0)
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);

        SceneDocument current = _scenes[index];
        SceneDocument changed = new(
            current.SceneId,
            current.Name,
            environment,
            current.BuddyPlacements,
            current.SchemaVersion);
        _scenes[index] = changed;
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, changed);
    }

    public SceneLibraryResult AddBuddy(
        SceneId sceneId,
        BuddyIdentityId buddyIdentityId,
        CanonicalRoomPosition position)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        if (!buddyIdentityId.IsValid)
            throw new ArgumentException("Adding a Buddy requires a stable Buddy identity.", nameof(buddyIdentityId));
        int index = IndexOf(sceneId);
        if (index < 0)
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);

        SceneDocument current = _scenes[index];
        if (current.BuddyPlacements.Any(p => p.BuddyIdentityId == buddyIdentityId))
            return new SceneLibraryResult(SceneLibraryStatus.BuddyAlreadyPresent, current);

        var placements = current.BuddyPlacements.ToList();
        placements.Add(new BuddyPlacement(NextUniquePlacementId(current), buddyIdentityId, position));
        SceneDocument changed = CopyScene(current, current.SceneId, current.Name, placements);
        _scenes[index] = changed;
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, changed);
    }

    public SceneLibraryResult RemoveBuddy(SceneId sceneId, BuddyIdentityId buddyIdentityId)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        int index = IndexOf(sceneId);
        if (index < 0)
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);

        SceneDocument current = _scenes[index];
        List<BuddyPlacement> placements = current.BuddyPlacements
            .Where(p => p.BuddyIdentityId != buddyIdentityId)
            .ToList();
        if (placements.Count == current.BuddyPlacements.Count)
            return new SceneLibraryResult(SceneLibraryStatus.BuddyNotFound, current);

        SceneDocument changed = CopyScene(current, current.SceneId, current.Name, placements);
        _scenes[index] = changed;
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, changed);
    }

    public SceneLibraryResult MoveBuddy(
        SceneId sceneId,
        BuddyPlacementId placementId,
        CanonicalRoomPosition position)
    {
        if (!_scope.IncludesScenes)
            return new SceneLibraryResult(SceneLibraryStatus.ScenesUnavailable);
        int index = IndexOf(sceneId);
        if (index < 0)
            return new SceneLibraryResult(SceneLibraryStatus.SceneNotFound);

        SceneDocument current = _scenes[index];
        int placementIndex = -1;
        for (int i = 0; i < current.BuddyPlacements.Count; i++)
        {
            if (current.BuddyPlacements[i].PlacementId == placementId)
            {
                placementIndex = i;
                break;
            }
        }
        if (placementIndex < 0)
            return new SceneLibraryResult(SceneLibraryStatus.BuddyNotFound, current);

        BuddyPlacement existing = current.BuddyPlacements[placementIndex];
        if (existing.Position.Equals(position))
            return new SceneLibraryResult(SceneLibraryStatus.NoChange, current);

        var placements = current.BuddyPlacements.ToArray();
        placements[placementIndex] = existing with { Position = position };
        SceneDocument changed = CopyScene(current, current.SceneId, current.Name, placements);
        _scenes[index] = changed;
        return new SceneLibraryResult(SceneLibraryStatus.Succeeded, changed);
    }

    private int IndexOf(SceneId sceneId)
    {
        for (int i = 0; i < _scenes.Count; i++)
        {
            if (_scenes[i].SceneId == sceneId)
                return i;
        }
        return -1;
    }

    private SceneId NextUniqueSceneId()
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            SceneId candidate = _newSceneId();
            if (!candidate.IsValid)
                throw new InvalidOperationException("Scene ID factory returned an invalid ID.");
            if (IndexOf(candidate) < 0)
                return candidate;
        }
        throw new InvalidOperationException("Scene ID factory repeatedly returned duplicate IDs.");
    }

    private BuddyPlacementId NextUniquePlacementId(SceneDocument scene)
    {
        for (int attempt = 0; attempt < 32; attempt++)
        {
            BuddyPlacementId candidate = _newPlacementId();
            if (!candidate.IsValid)
                throw new InvalidOperationException("Buddy placement ID factory returned an invalid ID.");
            if (!scene.BuddyPlacements.Any(p => p.PlacementId == candidate))
                return candidate;
        }
        throw new InvalidOperationException("Buddy placement ID factory repeatedly returned duplicate IDs.");
    }

    private SceneDocument CopyScene(
        SceneDocument source,
        SceneId sceneId,
        string name,
        bool preservePlacementIds)
    {
        IEnumerable<BuddyPlacement> placements = preservePlacementIds
            ? source.BuddyPlacements
            : source.BuddyPlacements.Select(p =>
                new BuddyPlacement(NextUniquePlacementIdForCopy(), p.BuddyIdentityId, p.Position));
        return CopyScene(source, sceneId, name, placements);
    }

    private static SceneDocument CopyScene(
        SceneDocument source,
        SceneId sceneId,
        string name,
        IEnumerable<BuddyPlacement> placements) =>
        new(sceneId, name, source.EnvironmentProgress, placements, source.SchemaVersion);

    private BuddyPlacementId NextUniquePlacementIdForCopy()
    {
        // Placement IDs are scoped to a Scene, but still require a valid non-empty factory result.
        BuddyPlacementId id = _newPlacementId();
        if (!id.IsValid)
            throw new InvalidOperationException("Buddy placement ID factory returned an invalid ID.");
        return id;
    }

    private string DuplicateName(string sourceName)
    {
        const string suffix = " Copy";
        string baseName = sourceName;
        if (baseName.Length + suffix.Length > SceneDocument.MaximumNameLength)
            baseName = baseName[..(SceneDocument.MaximumNameLength - suffix.Length)];
        string candidate = baseName + suffix;
        if (!_scenes.Any(scene => string.Equals(scene.Name, candidate, StringComparison.Ordinal)))
            return candidate;

        for (int number = 2; number < 10_000; number++)
        {
            string numberedSuffix = $" Copy {number}";
            int maxBase = SceneDocument.MaximumNameLength - numberedSuffix.Length;
            string numberedBase = sourceName.Length > maxBase ? sourceName[..maxBase] : sourceName;
            candidate = numberedBase + numberedSuffix;
            if (!_scenes.Any(scene => string.Equals(scene.Name, candidate, StringComparison.Ordinal)))
                return candidate;
        }
        throw new InvalidOperationException("Could not derive a unique duplicate Scene name.");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Persistence;

/// <summary>
/// One crash-consistent commit boundary for account progress, Work progress, Buddy identities and
/// Scene documents. Every new document is staged at its canonical path plus <c>.next</c>. The
/// manifest is then replaced atomically and becomes the commit point. Promotion to canonical paths
/// happens afterwards; a loader resolves each manifest hash from either the canonical file or its
/// staged sibling, so a process crash during promotion cannot expose a mixed generation.
/// </summary>
public sealed class SceneProgressTransactionStore
{
    public const string ManifestFileName = "scene-progress.commit.json";
    private const string StagedSuffix = ".next";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _saveRoot;
    private readonly string _manifestPath;
    private readonly IAtomicSaveFileSystem _files;

    public SceneProgressTransactionStore(string saveRoot, IAtomicSaveFileSystem files)
    {
        if (string.IsNullOrWhiteSpace(saveRoot))
            throw new ArgumentException("A resolved save root is required.", nameof(saveRoot));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _saveRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saveRoot));
        _manifestPath = Path.Combine(_saveRoot, ManifestFileName);
    }

    /// <summary>
    /// True when a Scene progress manifest exists. Callers must still load and validate it before
    /// treating the generation as usable; this is only the format discriminator used at bootstrap.
    /// </summary>
    public bool HasCommittedGeneration => _files.Exists(_manifestPath);

    public async Task<SceneProgressCommitResult> CommitAsync(
        PlayerProgressState player,
        WorkProgressState work,
        IReadOnlyCollection<BuddyIdentityState> buddyIdentities,
        IReadOnlyList<SceneDocument> scenes,
        SceneId activeSceneId,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(buddyIdentities);
        ArgumentNullException.ThrowIfNull(scenes);

        // Never overwrite a .next file that is still the only copy named by the current manifest.
        // Recovery must finish first or the new commit is rejected before any staging write occurs.
        await PersistenceWork.Run(EnsureCurrentCommitPromoted, token).ConfigureAwait(false);

        long revision = ReadCurrentManifest()?.Revision + 1 ?? 0;
        PreparedCommit prepared = Prepare(player, work, buddyIdentities, scenes, activeSceneId, revision);

        // Prepare phase: no authoritative pointer changes here. A crash/failure leaves the previous
        // manifest and its canonical generation untouched; abandoned .next files are overwritten by
        // a later retry.
        foreach (PreparedDocument document in prepared.Documents)
        {
            token.ThrowIfCancellationRequested();
            await PersistenceWork.Run(
                () => WriteStaged(document.CanonicalPath, document.Json), token).ConfigureAwait(false);
        }

        string manifestJson = JsonSerializer.Serialize(prepared.Manifest, JsonOptions);
        await PersistenceWork.Run(
            () => SaveManifestAtomic(manifestJson, token), token).ConfigureAwait(false);

        // Commit point has passed. Promotion is maintenance, not part of transaction success. Never
        // throw a post-commit I/O/cancellation failure to a caller that might roll back live state;
        // the next load/commit can recover from the manifest + remaining .next files.
        bool promoted = true;
        try
        {
            foreach (PreparedDocument document in prepared.Documents)
                Promote(document.CanonicalPath, document.Sha256);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            promoted = false;
        }

        return new SceneProgressCommitResult(revision, promoted);
    }

    public Task<SceneProgressLoadResult> LoadCommittedAsync(
        double cashPerPain,
        CancellationToken token = default) =>
        PersistenceWork.Run(() => LoadCommitted(cashPerPain, token), token);

    private SceneProgressLoadResult LoadCommitted(double cashPerPain, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        SceneProgressCommitManifest manifest = ReadCurrentManifest()
            ?? throw new FileNotFoundException("No committed Scene progress manifest exists.", _manifestPath);
        manifest.Validate();

        string playerJson = ReadCommitted(ProgressPath(), manifest.PlayerSha256);
        PlayerProgressDecodeResult playerDecoded = PlayerProgressSavePolicy.Decode(playerJson);
        if (playerDecoded.Status != SaveDecodeStatus.Valid || !playerDecoded.Snapshot.HasValue)
            throw new InvalidDataException($"Committed player progress is invalid: {playerDecoded.Detail}");
        var player = new PlayerProgressState(cashPerPain, playerDecoded.Snapshot.Value);

        string workJson = ReadCommitted(WorkProgressPath(), manifest.WorkSha256);
        WorkProgressDecodeResult workDecoded = WorkProgressSavePolicy.Decode(workJson);
        if (workDecoded.Status != SaveDecodeStatus.Valid || workDecoded.State is null)
            throw new InvalidDataException($"Committed Work progress is invalid: {workDecoded.Detail}");
        WorkProgressState work = workDecoded.State;

        string indexJson = ReadCommitted(SceneIndexPath(), manifest.SceneIndexSha256);
        SceneIndexDecodeResult indexDecoded = SceneSavePolicy.DecodeIndex(indexJson);
        if (indexDecoded.Status != SaveDecodeStatus.Valid || indexDecoded.Index is null)
            throw new InvalidDataException($"Committed Scene index is invalid: {indexDecoded.Detail}");
        SceneIndexSave index = indexDecoded.Index;
        if (index.Revision != manifest.Revision)
            throw new InvalidDataException("Scene index revision does not match the committed generation.");

        var scenes = new List<SceneDocument>(manifest.Scenes.Count);
        foreach (SceneProgressSceneCommit entry in manifest.Scenes)
        {
            token.ThrowIfCancellationRequested();
            string json = ReadCommitted(SceneDocumentPath(SceneId.From(entry.SceneId)), entry.Sha256);
            SceneDocumentDecodeResult decoded = SceneSavePolicy.DecodeScene(json);
            if (decoded.Status != SaveDecodeStatus.Valid || decoded.Scene is null || decoded.Scene.SceneId.Value != entry.SceneId)
                throw new InvalidDataException($"Committed Scene {entry.SceneId:N} is invalid: {decoded.Detail}");
            scenes.Add(decoded.Scene);
        }

        var buddies = new List<BuddyIdentityState>(manifest.Buddies.Count);
        var buddyIds = new HashSet<BuddyIdentityId>();
        foreach (SceneProgressBuddyCommit entry in manifest.Buddies)
        {
            token.ThrowIfCancellationRequested();
            BuddyIdentityId id = BuddyIdentityId.From(entry.BuddyIdentityId);
            string json = ReadCommitted(BuddyIdentityPath(id), entry.Sha256);
            BuddyIdentityDecodeResult decoded = BuddyIdentitySavePolicy.Decode(json);
            if (decoded.Status != SaveDecodeStatus.Valid || decoded.State is null || decoded.State.BuddyIdentityId != id)
                throw new InvalidDataException($"Committed Buddy {entry.BuddyIdentityId:N} is invalid: {decoded.Detail}");
            buddies.Add(decoded.State);
            buddyIds.Add(id);
        }

        ValidateLoadedGraph(manifest, index, scenes, buddyIds);
        return new SceneProgressLoadResult(player, work, buddies, scenes, index, manifest.Revision);
    }

    private PreparedCommit Prepare(
        PlayerProgressState player,
        WorkProgressState work,
        IReadOnlyCollection<BuddyIdentityState> buddies,
        IReadOnlyList<SceneDocument> scenes,
        SceneId activeSceneId,
        long revision)
    {
        if (!activeSceneId.IsValid)
            throw new ArgumentException("A committed Scene library requires an active Scene.", nameof(activeSceneId));
        if (scenes.Count == 0)
            throw new ArgumentException("A committed Scene library requires at least one Scene.", nameof(scenes));

        var sceneIds = new HashSet<SceneId>();
        foreach (SceneDocument scene in scenes)
        {
            ArgumentNullException.ThrowIfNull(scene);
            if (!sceneIds.Add(scene.SceneId))
                throw new ArgumentException("Scene commit contains duplicate Scene IDs.", nameof(scenes));
        }
        if (!sceneIds.Contains(activeSceneId))
            throw new ArgumentException("Active Scene must appear in the committed Scene list.", nameof(activeSceneId));

        var buddyMap = new Dictionary<BuddyIdentityId, BuddyIdentityState>();
        foreach (BuddyIdentityState buddy in buddies)
        {
            ArgumentNullException.ThrowIfNull(buddy);
            if (!buddyMap.TryAdd(buddy.BuddyIdentityId, buddy))
                throw new ArgumentException("Scene commit contains duplicate Buddy identities.", nameof(buddies));
        }
        foreach (SceneDocument scene in scenes)
        {
            foreach (BuddyPlacement placement in scene.BuddyPlacements)
            {
                if (!buddyMap.ContainsKey(placement.BuddyIdentityId))
                    throw new ArgumentException("A Scene placement references a Buddy identity not included in the commit.", nameof(buddies));
            }
        }

        var documents = new List<PreparedDocument>(3 + scenes.Count + buddies.Count);
        PreparedDocument playerDocument = PrepareDocument(ProgressPath(), PlayerProgressSavePolicy.Serialize(player));
        documents.Add(playerDocument);
        PreparedDocument workDocument = PrepareDocument(WorkProgressPath(), WorkProgressSavePolicy.Serialize(work));
        documents.Add(workDocument);

        var buddyEntries = new List<SceneProgressBuddyCommit>(buddyMap.Count);
        foreach (BuddyIdentityState buddy in buddyMap.Values.OrderBy(value => value.BuddyIdentityId))
        {
            PreparedDocument document = PrepareDocument(BuddyIdentityPath(buddy.BuddyIdentityId), BuddyIdentitySavePolicy.Serialize(buddy));
            documents.Add(document);
            buddyEntries.Add(new SceneProgressBuddyCommit(buddy.BuddyIdentityId.Value, document.Sha256));
        }

        var sceneEntries = new List<SceneProgressSceneCommit>(scenes.Count);
        foreach (SceneDocument scene in scenes)
        {
            PreparedDocument document = PrepareDocument(SceneDocumentPath(scene.SceneId), SceneSavePolicy.SerializeScene(scene));
            documents.Add(document);
            sceneEntries.Add(new SceneProgressSceneCommit(scene.SceneId.Value, document.Sha256));
        }

        var index = new SceneIndexSave
        {
            Revision = revision,
            ActiveSceneId = activeSceneId.Value,
            OrderedSceneIds = scenes.Select(scene => scene.SceneId.Value).ToList(),
        };
        PreparedDocument indexDocument = PrepareDocument(SceneIndexPath(), SceneSavePolicy.SerializeIndex(index));
        documents.Add(indexDocument);

        var manifest = new SceneProgressCommitManifest
        {
            Revision = revision,
            PlayerSha256 = playerDocument.Sha256,
            WorkSha256 = workDocument.Sha256,
            SceneIndexSha256 = indexDocument.Sha256,
            Scenes = sceneEntries,
            Buddies = buddyEntries,
        };
        manifest.Validate();
        return new PreparedCommit(manifest, documents);
    }

    private void EnsureCurrentCommitPromoted()
    {
        SceneProgressCommitManifest? manifest = ReadCurrentManifest();
        if (manifest is null)
            return;
        manifest.Validate();

        Promote(ProgressPath(), manifest.PlayerSha256);
        Promote(WorkProgressPath(), manifest.WorkSha256);
        Promote(SceneIndexPath(), manifest.SceneIndexSha256);
        foreach (SceneProgressSceneCommit scene in manifest.Scenes)
            Promote(SceneDocumentPath(SceneId.From(scene.SceneId)), scene.Sha256);
        foreach (SceneProgressBuddyCommit buddy in manifest.Buddies)
            Promote(BuddyIdentityPath(BuddyIdentityId.From(buddy.BuddyIdentityId)), buddy.Sha256);
    }

    private void Promote(string canonicalPath, string expectedSha256)
    {
        if (_files.Exists(canonicalPath) && Hash(_files.ReadAllText(canonicalPath)) == expectedSha256)
            return;

        string staged = canonicalPath + StagedSuffix;
        if (!_files.Exists(staged) || Hash(_files.ReadAllText(staged)) != expectedSha256)
            throw new InvalidDataException($"Committed document is unavailable at {canonicalPath}.");

        string directory = Path.GetDirectoryName(canonicalPath)
            ?? throw new InvalidOperationException("Save path has no parent directory.");
        _files.CreateDirectory(directory);
        if (_files.Exists(canonicalPath))
            _files.Replace(staged, canonicalPath, canonicalPath + ".bak");
        else
            _files.Move(staged, canonicalPath);
    }

    private string ReadCommitted(string canonicalPath, string expectedSha256)
    {
        if (_files.Exists(canonicalPath))
        {
            string canonical = _files.ReadAllText(canonicalPath);
            if (Hash(canonical) == expectedSha256)
                return canonical;
        }

        string stagedPath = canonicalPath + StagedSuffix;
        if (_files.Exists(stagedPath))
        {
            string staged = _files.ReadAllText(stagedPath);
            if (Hash(staged) == expectedSha256)
                return staged;
        }

        throw new InvalidDataException($"Neither canonical nor staged bytes match the committed hash for {canonicalPath}.");
    }

    private SceneProgressCommitManifest? ReadCurrentManifest()
    {
        if (!_files.Exists(_manifestPath))
            return null;
        try
        {
            SceneProgressCommitManifest manifest = JsonSerializer.Deserialize<SceneProgressCommitManifest>(
                _files.ReadAllText(_manifestPath), JsonOptions)
                ?? throw new InvalidDataException("Scene progress manifest was null.");
            manifest.Validate();
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Scene progress manifest is malformed.", exception);
        }
    }

    private void WriteStaged(string canonicalPath, string json)
    {
        string directory = Path.GetDirectoryName(canonicalPath)
            ?? throw new InvalidOperationException("Save path has no parent directory.");
        _files.CreateDirectory(directory);
        _files.WriteDurable(canonicalPath + StagedSuffix, json);
    }

    private void SaveManifestAtomic(string json, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _files.CreateDirectory(_saveRoot);
        string temporary = _manifestPath + ".tmp";
        _files.WriteDurable(temporary, json);
        token.ThrowIfCancellationRequested();
        if (_files.Exists(_manifestPath))
            _files.Replace(temporary, _manifestPath, _manifestPath + ".bak");
        else
            _files.Move(temporary, _manifestPath);
    }

    private string ProgressPath() => Path.Combine(_saveRoot, SteamCloudSavePolicy.ProgressFileName);
    private string WorkProgressPath() => ResolveRelative(SceneStoragePaths.WorkProgress);
    private string SceneIndexPath() => ResolveRelative(SceneStoragePaths.SceneIndex);
    private string SceneDocumentPath(SceneId id) => ResolveRelative(SceneStoragePaths.SceneDocument(id));
    private string BuddyIdentityPath(BuddyIdentityId id) => ResolveRelative(SceneStoragePaths.BuddyIdentity(id));

    private string ResolveRelative(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(_saveRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        string back = Path.GetRelativePath(_saveRoot, full);
        if (back == ".." || back.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Scene storage path escaped the trusted save root.");
        return full;
    }

    private static PreparedDocument PrepareDocument(string canonicalPath, string json) =>
        new(canonicalPath, json, Hash(json));

    private static string Hash(string text)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(digest);
    }

    private static void ValidateLoadedGraph(
        SceneProgressCommitManifest manifest,
        SceneIndexSave index,
        IReadOnlyList<SceneDocument> scenes,
        IReadOnlySet<BuddyIdentityId> buddyIds)
    {
        if (index.OrderedSceneIds.Count != scenes.Count || manifest.Scenes.Count != scenes.Count)
            throw new InvalidDataException("Committed Scene index cardinality is inconsistent.");
        for (int i = 0; i < scenes.Count; i++)
        {
            if (index.OrderedSceneIds[i] != scenes[i].SceneId.Value || manifest.Scenes[i].SceneId != scenes[i].SceneId.Value)
                throw new InvalidDataException("Committed Scene ordering is inconsistent.");
        }
        if (index.ActiveSceneId == Guid.Empty || !index.OrderedSceneIds.Contains(index.ActiveSceneId))
            throw new InvalidDataException("Committed active Scene is invalid.");
        foreach (SceneDocument scene in scenes)
        {
            foreach (BuddyPlacement placement in scene.BuddyPlacements)
            {
                if (!buddyIds.Contains(placement.BuddyIdentityId))
                    throw new InvalidDataException("Committed Scene references a missing Buddy identity.");
            }
        }
    }

    private sealed record PreparedDocument(string CanonicalPath, string Json, string Sha256);
    private sealed record PreparedCommit(SceneProgressCommitManifest Manifest, IReadOnlyList<PreparedDocument> Documents);
}

public sealed record SceneProgressCommitResult(long Revision, bool CanonicalPromotionComplete);

public sealed record SceneProgressLoadResult(
    PlayerProgressState Player,
    WorkProgressState Work,
    IReadOnlyList<BuddyIdentityState> BuddyIdentities,
    IReadOnlyList<SceneDocument> Scenes,
    SceneIndexSave Index,
    long Revision);

public sealed record SceneProgressSceneCommit(Guid SceneId, string Sha256);
public sealed record SceneProgressBuddyCommit(Guid BuddyIdentityId, string Sha256);

public sealed record SceneProgressCommitManifest
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public string PlayerSha256 { get; init; } = string.Empty;
    public string WorkSha256 { get; init; } = string.Empty;
    public string SceneIndexSha256 { get; init; } = string.Empty;
    public List<SceneProgressSceneCommit> Scenes { get; init; } = [];
    public List<SceneProgressBuddyCommit> Buddies { get; init; } = [];

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion || Revision < 0 ||
            !IsSha256(PlayerSha256) || !IsSha256(WorkSha256) || !IsSha256(SceneIndexSha256) ||
            Scenes is null || Scenes.Count == 0 || Buddies is null)
            throw new InvalidDataException("Scene progress commit manifest is invalid.");

        var sceneIds = new HashSet<Guid>();
        foreach (SceneProgressSceneCommit scene in Scenes)
        {
            if (scene is null || scene.SceneId == Guid.Empty || !sceneIds.Add(scene.SceneId) || !IsSha256(scene.Sha256))
                throw new InvalidDataException("Scene progress manifest contains an invalid Scene entry.");
        }

        var buddyIds = new HashSet<Guid>();
        foreach (SceneProgressBuddyCommit buddy in Buddies)
        {
            if (buddy is null || buddy.BuddyIdentityId == Guid.Empty || !buddyIds.Add(buddy.BuddyIdentityId) || !IsSha256(buddy.Sha256))
                throw new InvalidDataException("Scene progress manifest contains an invalid Buddy entry.");
        }
    }

    private static bool IsSha256(string value)
    {
        if (value is null || value.Length != 64)
            return false;
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }
        return true;
    }
}

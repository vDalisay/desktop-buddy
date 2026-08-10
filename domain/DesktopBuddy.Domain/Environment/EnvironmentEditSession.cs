using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Environment;

public enum EnvironmentEditStatus
{
    Succeeded, UnknownDefinition, HiddenDefinition, UnknownInstance, InvalidPlacement,
    RotationNotAllowed, InsufficientFunds, LayoutFull, ArithmeticOverflow, AlreadyReserved, NoReservation,
}

public readonly record struct EnvironmentEditResult(EnvironmentEditStatus Status, PlacedDecorationId InstanceId = default)
{
    public bool Succeeded => Status == EnvironmentEditStatus.Succeeded;
}

public readonly record struct EnvironmentCommit(
    EnvironmentLayout Layout,
    long BalanceMilliCredits,
    IReadOnlyList<DecorationDefinitionId> OwnedUnplaced);

/// <summary>Opaque undo point for one editing pass; produced and consumed by the session.</summary>
public sealed record EnvironmentEditCheckpoint(
    IReadOnlyList<PlacedDecoration> Working,
    IReadOnlyDictionary<DecorationDefinitionId, int> Owned,
    IReadOnlyCollection<PlacedDecorationId> StagedFromStorage,
    long PendingDelta);

public sealed class EnvironmentEditSession
{
    private readonly EnvironmentLayout _baseline;
    private readonly DecorationCatalogue _catalogue;
    private readonly Func<PlacedDecorationId> _createInstanceId;
    private readonly Dictionary<DecorationDefinitionId, int> _baselineOwned;
    private Dictionary<DecorationDefinitionId, int> _owned;
    private List<PlacedDecoration> _working;
    private long _pendingDelta;
    private DecorationDefinitionId _reservedDefinitionId;
    private PlacedDecorationId _reservedInstanceId;
    private bool _reservedFromStorage;
    private readonly HashSet<PlacedDecorationId> _stagedFromStorage = [];

    public EnvironmentEditSession(EnvironmentLayout baseline, long startingBalanceMilliCredits,
        DecorationCatalogue catalogue, Func<PlacedDecorationId>? createInstanceId = null,
        IEnumerable<DecorationDefinitionId>? ownedUnplaced = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(catalogue);
        if (startingBalanceMilliCredits < 0) throw new ArgumentOutOfRangeException(nameof(startingBalanceMilliCredits));
        _baseline = baseline;
        _working = baseline.Decorations.ToList();
        _catalogue = catalogue;
        _createInstanceId = createInstanceId ?? PlacedDecorationId.New;
        _baselineOwned = Tally(ownedUnplaced);
        _owned = new Dictionary<DecorationDefinitionId, int>(_baselineOwned);
        StartingBalanceMilliCredits = startingBalanceMilliCredits;
    }

    /// <summary>Copies the player owns but has not placed. Placing one of these costs nothing.</summary>
    public IReadOnlyList<DecorationDefinitionId> OwnedUnplaced => Flatten(_owned);
    public int OwnedUnplacedCount(DecorationDefinitionId id) => _owned.TryGetValue(id, out int count) ? count : 0;

    public long StartingBalanceMilliCredits { get; }
    public long PendingBalanceDeltaMilliCredits => _pendingDelta;
    public long ProjectedBalanceMilliCredits => StartingBalanceMilliCredits + _pendingDelta;
    public bool TryProjectBalance(long currentBalanceMilliCredits, out long projectedBalanceMilliCredits)
    {
        projectedBalanceMilliCredits = 0;
        if (currentBalanceMilliCredits < 0) return false;
        try
        {
            projectedBalanceMilliCredits = checked(currentBalanceMilliCredits + _pendingDelta);
            return projectedBalanceMilliCredits >= 0;
        }
        catch (OverflowException) { return false; }
    }
    public bool HasReservation => _reservedInstanceId != default;
    public DecorationDefinitionId ReservedDefinitionId => _reservedDefinitionId;
    public bool IsDirty => HasReservation || _pendingDelta != 0 || !_working.SequenceEqual(_baseline.Decorations) ||
        _owned.Count != _baselineOwned.Count ||
        _owned.Any(entry => !_baselineOwned.TryGetValue(entry.Key, out int count) || count != entry.Value);
    public bool MatchesBaseline(EnvironmentLayout layout) =>
        layout.SchemaVersion == _baseline.SchemaVersion && layout.Decorations.SequenceEqual(_baseline.Decorations);
    public EnvironmentLayout WorkingLayout => new(_working);

    public EnvironmentEditResult Place(DecorationDefinitionId definitionId, CanonicalRoomPosition position)
    {
        EnvironmentEditResult reserved = Reserve(definitionId, StartingBalanceMilliCredits);
        if (!reserved.Succeeded) return reserved;
        EnvironmentEditResult placed = PlaceReserved(position);
        if (!placed.Succeeded) CancelReservation();
        return placed;
    }

    public EnvironmentEditResult Reserve(DecorationDefinitionId definitionId, long currentBalanceMilliCredits)
    {
        if (HasReservation) return new(EnvironmentEditStatus.AlreadyReserved);
        if (!_catalogue.TryGet(definitionId, out DecorationDefinition definition)) return new(EnvironmentEditStatus.UnknownDefinition);
        if (!definition.Visible) return new(EnvironmentEditStatus.HiddenDefinition);
        if (_working.Count >= EnvironmentLayout.MaximumPlacedDecorations) return new(EnvironmentEditStatus.LayoutFull);
        PlacedDecorationId instanceId = _createInstanceId();
        if (instanceId == default || _working.Any(item => item.InstanceId == instanceId)) return new(EnvironmentEditStatus.InvalidPlacement);
        // An owned copy in storage is placed for free; only a brand-new copy costs credits.
        bool fromStorage = OwnedUnplacedCount(definition.Id) > 0;
        if (fromStorage) TakeFromStorage(definition.Id);
        else if (!TryChangeDelta(-definition.PriceMilliCredits, currentBalanceMilliCredits))
            return new(EnvironmentEditStatus.InsufficientFunds);
        _reservedFromStorage = fromStorage;
        _reservedDefinitionId = definition.Id;
        _reservedInstanceId = instanceId;
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public EnvironmentEditResult PlaceReserved(CanonicalRoomPosition position)
    {
        if (!HasReservation) return new(EnvironmentEditStatus.NoReservation);
        if (!_catalogue.TryGet(_reservedDefinitionId, out DecorationDefinition definition)) return new(EnvironmentEditStatus.UnknownDefinition);
        if (_working.Count >= EnvironmentLayout.MaximumPlacedDecorations) return new(EnvironmentEditStatus.LayoutFull);
        // The room has one wallpaper slot. Replacing a saved wallpaper is a final new purchase;
        // replacing one staged in this session only cancels that uncommitted placement.
        if (definition.RenderBand == DecorationRenderBand.Wallpaper)
        {
            int occupied = _working.FindIndex(item => item.RenderBand == DecorationRenderBand.Wallpaper);
            if (occupied >= 0)
            {
                if (!ReleasePlaced(_working[occupied])) return new(EnvironmentEditStatus.ArithmeticOverflow);
                _working.RemoveAt(occupied);
            }
        }
        PlacedDecorationId instanceId = _reservedInstanceId;
        if (_reservedFromStorage) _stagedFromStorage.Add(instanceId);
        _working.Add(new PlacedDecoration(instanceId, definition.Id, position, 0, definition.RenderBand, definition.PriceMilliCredits));
        ClearReservation();
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public EnvironmentEditResult CancelReservation()
    {
        if (!HasReservation) return new(EnvironmentEditStatus.NoReservation);
        if (!_catalogue.TryGet(_reservedDefinitionId, out DecorationDefinition definition)) return new(EnvironmentEditStatus.UnknownDefinition);
        if (_reservedFromStorage) ReturnToStorage(_reservedDefinitionId);
        else if (!TryChangeDelta(definition.PriceMilliCredits, StartingBalanceMilliCredits))
            return new(EnvironmentEditStatus.ArithmeticOverflow);
        PlacedDecorationId instanceId = _reservedInstanceId;
        ClearReservation();
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public EnvironmentEditResult Move(PlacedDecorationId instanceId, CanonicalRoomPosition position)
    {
        int index = Find(instanceId);
        if (index < 0) return new(EnvironmentEditStatus.UnknownInstance);
        _working[index] = _working[index] with { Position = position };
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public EnvironmentEditResult Rotate(PlacedDecorationId instanceId, int direction = 1)
    {
        int index = Find(instanceId);
        if (index < 0) return new(EnvironmentEditStatus.UnknownInstance);
        PlacedDecoration placed = _working[index];
        if (!_catalogue.TryGet(placed.DefinitionId, out DecorationDefinition definition)) return new(EnvironmentEditStatus.UnknownDefinition);
        if (!definition.Rotation.AllowsRotation) return new(EnvironmentEditStatus.RotationNotAllowed);
        int rotation = (placed.RotationDegrees + (definition.Rotation.StepDegrees * Math.Sign(direction))) % 360;
        if (rotation < 0) rotation += 360;
        _working[index] = placed with { RotationDegrees = rotation };
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    /// <summary>
    /// Captures everything an editing pass can change — layout, storage, and staged credits — so a
    /// Cancel inside that pass can undo moves, rotations and deletions together.
    /// </summary>
    public EnvironmentEditCheckpoint Checkpoint() =>
        new(_working.ToList(), new Dictionary<DecorationDefinitionId, int>(_owned),
            new HashSet<PlacedDecorationId>(_stagedFromStorage), _pendingDelta);

    public void Restore(EnvironmentEditCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        _working = checkpoint.Working.ToList();
        _owned = new Dictionary<DecorationDefinitionId, int>(checkpoint.Owned);
        _stagedFromStorage.Clear();
        foreach (PlacedDecorationId id in checkpoint.StagedFromStorage) _stagedFromStorage.Add(id);
        _pendingDelta = checkpoint.PendingDelta;
        ClearReservation();
    }

    /// <summary>
    /// Removes any placed decoration. A purchase is owned forever, so a copy that was already paid
    /// for goes back into storage and can be placed again for free. The one exception is a copy
    /// bought during this still-open session: that purchase is only staged, so deleting it restores
    /// the credits instead of banking the copy.
    /// </summary>
    public EnvironmentEditResult Remove(PlacedDecorationId instanceId)
    {
        int index = Find(instanceId);
        if (index < 0) return new(EnvironmentEditStatus.UnknownInstance);
        if (!ReleasePlaced(_working[index])) return new(EnvironmentEditStatus.ArithmeticOverflow);
        _working.RemoveAt(index);
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    /// <summary>Banks or refunds a decoration leaving the room. False only on arithmetic overflow.</summary>
    private bool ReleasePlaced(PlacedDecoration placed)
    {
        bool existedAtOpen = _baseline.Decorations.Any(item => item.InstanceId == placed.InstanceId);
        if (existedAtOpen || _stagedFromStorage.Remove(placed.InstanceId))
        {
            ReturnToStorage(placed.DefinitionId);
            return true;
        }
        return TryChangeDelta(placed.PurchasePriceMilliCredits);
    }

    public EnvironmentEditResult RemoveStaged(PlacedDecorationId instanceId)
    {
        int index = Find(instanceId);
        if (index < 0) return new(EnvironmentEditStatus.UnknownInstance);
        if (_baseline.Decorations.Any(item => item.InstanceId == instanceId))
            return new(EnvironmentEditStatus.UnknownInstance);
        return Remove(instanceId);
    }

    public void Cancel()
    {
        _working = _baseline.Decorations.ToList();
        _owned = new Dictionary<DecorationDefinitionId, int>(_baselineOwned);
        _stagedFromStorage.Clear();
        _pendingDelta = 0;
        ClearReservation();
    }
    public EnvironmentCommit PrepareCommit() =>
        TryPrepareCommit(StartingBalanceMilliCredits, out EnvironmentCommit commit)
            ? commit
            : throw new InvalidOperationException("The staged environment transaction cannot be committed.");
    public bool TryPrepareCommit(long currentBalanceMilliCredits, out EnvironmentCommit commit)
    {
        commit = default;
        if (HasReservation || !TryProjectBalance(currentBalanceMilliCredits, out long balance)) return false;
        commit = new EnvironmentCommit(new EnvironmentLayout(_working), balance, OwnedUnplaced);
        return true;
    }
    private int Find(PlacedDecorationId id) => _working.FindIndex(item => item.InstanceId == id);

    private static Dictionary<DecorationDefinitionId, int> Tally(IEnumerable<DecorationDefinitionId>? ids)
    {
        var tally = new Dictionary<DecorationDefinitionId, int>();
        foreach (DecorationDefinitionId id in ids ?? [])
        {
            if (id == default) continue;
            tally[id] = tally.TryGetValue(id, out int count) ? count + 1 : 1;
        }
        return tally;
    }

    private static IReadOnlyList<DecorationDefinitionId> Flatten(Dictionary<DecorationDefinitionId, int> tally) =>
        tally.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal)
            .SelectMany(entry => Enumerable.Repeat(entry.Key, entry.Value))
            .ToArray();

    private void TakeFromStorage(DecorationDefinitionId id)
    {
        int remaining = _owned[id] - 1;
        if (remaining > 0) _owned[id] = remaining; else _owned.Remove(id);
    }

    private void ReturnToStorage(DecorationDefinitionId id) =>
        _owned[id] = _owned.TryGetValue(id, out int count) ? count + 1 : 1;

    private bool TryChangeDelta(long change, long balanceBasis)
    {
        try
        {
            long candidateDelta = checked(_pendingDelta + change);
            long candidateBalance = checked(balanceBasis + candidateDelta);
            if (candidateBalance < 0) return false;
            _pendingDelta = candidateDelta;
            return true;
        }
        catch (OverflowException) { return false; }
    }

    private bool TryChangeDelta(long change) => TryChangeDelta(change, StartingBalanceMilliCredits);
    private void ClearReservation() { _reservedDefinitionId = default; _reservedInstanceId = default; _reservedFromStorage = false; }
}
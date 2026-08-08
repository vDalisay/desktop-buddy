using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Environment;

public enum EnvironmentEditStatus
{
    Succeeded, UnknownDefinition, HiddenDefinition, UnknownInstance, InvalidPlacement,
    RotationNotAllowed, InsufficientFunds, LayoutFull, ArithmeticOverflow,
}

public readonly record struct EnvironmentEditResult(EnvironmentEditStatus Status, PlacedDecorationId InstanceId = default)
{
    public bool Succeeded => Status == EnvironmentEditStatus.Succeeded;
}

public readonly record struct EnvironmentCommit(EnvironmentLayout Layout, long BalanceMilliCredits);

public static class DecorationEconomyPolicy
{
    public const int SellRefundPermille = 1000;
    public static bool TryRefund(long purchasePriceMilliCredits, out long refund)
    {
        try
        {
            refund = checked(purchasePriceMilliCredits * SellRefundPermille / 1000);
            return purchasePriceMilliCredits >= 0;
        }
        catch (OverflowException) { refund = 0; return false; }
    }
}

public sealed class EnvironmentEditSession
{
    private readonly EnvironmentLayout _baseline;
    private readonly HashSet<PlacedDecorationId> _baselineIds;
    private readonly DecorationCatalogue _catalogue;
    private readonly Func<PlacedDecorationId> _createInstanceId;
    private List<PlacedDecoration> _working;
    private long _pendingDelta;

    public EnvironmentEditSession(EnvironmentLayout baseline, long startingBalanceMilliCredits,
        DecorationCatalogue catalogue, Func<PlacedDecorationId>? createInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(catalogue);
        if (startingBalanceMilliCredits < 0) throw new ArgumentOutOfRangeException(nameof(startingBalanceMilliCredits));
        _baseline = baseline;
        _working = baseline.Decorations.ToList();
        _baselineIds = baseline.Decorations.Select(item => item.InstanceId).ToHashSet();
        _catalogue = catalogue;
        _createInstanceId = createInstanceId ?? PlacedDecorationId.New;
        StartingBalanceMilliCredits = startingBalanceMilliCredits;
    }

    public long StartingBalanceMilliCredits { get; }
    public long PendingBalanceDeltaMilliCredits => _pendingDelta;
    public long ProjectedBalanceMilliCredits => StartingBalanceMilliCredits + _pendingDelta;
    public bool IsDirty => _pendingDelta != 0 || !_working.SequenceEqual(_baseline.Decorations);
    public bool MatchesBaseline(EnvironmentLayout layout) =>
        layout.SchemaVersion == _baseline.SchemaVersion && layout.Decorations.SequenceEqual(_baseline.Decorations);
    public EnvironmentLayout WorkingLayout => new(_working);

    public EnvironmentEditResult Place(DecorationDefinitionId definitionId, CanonicalRoomPosition position)
    {
        if (!_catalogue.TryGet(definitionId, out DecorationDefinition definition)) return new(EnvironmentEditStatus.UnknownDefinition);
        if (!definition.Visible) return new(EnvironmentEditStatus.HiddenDefinition);
        if (_working.Count >= EnvironmentLayout.MaximumPlacedDecorations) return new(EnvironmentEditStatus.LayoutFull);
        if (definition.Category == DecorationCategory.Wallpaper && _working.Any(item => item.RenderBand == DecorationRenderBand.Wallpaper))
            return new(EnvironmentEditStatus.InvalidPlacement);
        if (!TryChangeDelta(-definition.PriceMilliCredits)) return new(EnvironmentEditStatus.InsufficientFunds);

        PlacedDecorationId instanceId = _createInstanceId();
        if (instanceId == default || _working.Any(item => item.InstanceId == instanceId))
        {
            _pendingDelta += definition.PriceMilliCredits;
            return new(EnvironmentEditStatus.InvalidPlacement);
        }
        _working.Add(new PlacedDecoration(instanceId, definition.Id, position, 0, definition.RenderBand, definition.PriceMilliCredits));
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public EnvironmentEditResult Move(PlacedDecorationId instanceId, CanonicalRoomPosition position)
    {
        int index = Find(instanceId);
        if (index < 0) return new(EnvironmentEditStatus.UnknownInstance);
        _working[index] = _working[index] with { Position = position };
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public EnvironmentEditResult Rotate(PlacedDecorationId instanceId)
    {
        int index = Find(instanceId);
        if (index < 0) return new(EnvironmentEditStatus.UnknownInstance);
        PlacedDecoration placed = _working[index];
        if (!_catalogue.TryGet(placed.DefinitionId, out DecorationDefinition definition)) return new(EnvironmentEditStatus.UnknownDefinition);
        if (!definition.Rotation.AllowsRotation) return new(EnvironmentEditStatus.RotationNotAllowed);
        _working[index] = placed with { RotationDegrees = (placed.RotationDegrees + definition.Rotation.StepDegrees) % 360 };
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public EnvironmentEditResult Sell(PlacedDecorationId instanceId)
    {
        int index = Find(instanceId);
        if (index < 0) return new(EnvironmentEditStatus.UnknownInstance);
        PlacedDecoration placed = _working[index];
        long credit;
        if (_baselineIds.Contains(instanceId))
        {
            if (!DecorationEconomyPolicy.TryRefund(placed.PurchasePriceMilliCredits, out credit))
                return new(EnvironmentEditStatus.ArithmeticOverflow);
        }
        else credit = placed.PurchasePriceMilliCredits;
        if (!TryChangeDelta(credit)) return new(EnvironmentEditStatus.ArithmeticOverflow);
        _working.RemoveAt(index);
        return new(EnvironmentEditStatus.Succeeded, instanceId);
    }

    public void Cancel() { _working = _baseline.Decorations.ToList(); _pendingDelta = 0; }
    public EnvironmentCommit PrepareCommit() => new(new EnvironmentLayout(_working), ProjectedBalanceMilliCredits);
    private int Find(PlacedDecorationId id) => _working.FindIndex(item => item.InstanceId == id);

    private bool TryChangeDelta(long change)
    {
        try
        {
            long candidateDelta = checked(_pendingDelta + change);
            long candidateBalance = checked(StartingBalanceMilliCredits + candidateDelta);
            if (candidateBalance < 0) return false;
            _pendingDelta = candidateDelta;
            return true;
        }
        catch (OverflowException) { return false; }
    }
}

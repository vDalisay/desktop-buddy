using System;
using System.Collections.Generic;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// Stable command IDs for the top-level Customize menu. Feature branches register against
/// these IDs instead of editing <see cref="Win98CommandBarBootstrap"/> directly.
/// </summary>
public static class CustomizeCommandIds
{
    public const string PaintBuddy = "customize.paint_buddy";
    public const string PaintBackground = "customize.paint_background";
    public const string BuddyStudio = "customize.buddy_studio";

    public const int PaintBuddyOrder = 100;
    public const int PaintBackgroundOrder = 200;
    public const int BuddyStudioOrder = 300;
}

public readonly record struct CustomizeCommandDefinition(
    string Id,
    string Label,
    string Tooltip,
    int Order);

public readonly record struct CustomizeCommandSnapshot(
    CustomizeCommandDefinition Definition,
    bool Visible,
    bool Enabled);

/// <summary>
/// Runtime-only registry for Customize workspace commands. The registry owns no workspace
/// lifecycle and no domain state; it is only the menu-routing seam that lets independently
/// developed features contribute commands without modifying the shared command bar.
/// </summary>
public sealed class CustomizeCommandRegistry
{
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly List<Entry> _orderedEntries = [];

    public event Action? Changed;

    public IDisposable Register(
        CustomizeCommandDefinition definition,
        Action invoke,
        Func<bool>? isVisible = null,
        Func<bool>? isEnabled = null)
    {
        if (string.IsNullOrWhiteSpace(definition.Id))
            throw new ArgumentException("Customize command ID is required.", nameof(definition));
        if (string.IsNullOrWhiteSpace(definition.Label))
            throw new ArgumentException("Customize command label is required.", nameof(definition));
        ArgumentNullException.ThrowIfNull(invoke);
        if (_entries.ContainsKey(definition.Id))
            throw new InvalidOperationException(
                $"Customize command '{definition.Id}' is already registered. Each route has one owner.");

        var entry = new Entry(definition, invoke, isVisible, isEnabled);
        _entries.Add(definition.Id, entry);
        _orderedEntries.Add(entry);
        _orderedEntries.Sort(CompareEntries);
        Changed?.Invoke();
        return new Registration(this, definition.Id);
    }

    /// <summary>
    /// Returns the stable authored order while evaluating visibility/enabled policy at read time.
    /// Registration is rare; snapshots may be requested every frame, so sorting belongs on the
    /// mutation path rather than the presentation hot path.
    /// </summary>
    public IReadOnlyList<CustomizeCommandSnapshot> Snapshot()
    {
        var snapshots = new CustomizeCommandSnapshot[_orderedEntries.Count];
        for (int index = 0; index < _orderedEntries.Count; index++)
        {
            Entry entry = _orderedEntries[index];
            snapshots[index] = new CustomizeCommandSnapshot(
                entry.Definition,
                entry.IsVisible?.Invoke() ?? true,
                entry.IsEnabled?.Invoke() ?? true);
        }
        return snapshots;
    }

    public bool TryInvoke(string id)
    {
        if (!_entries.TryGetValue(id, out Entry? entry))
            return false;
        if (!(entry.IsVisible?.Invoke() ?? true) || !(entry.IsEnabled?.Invoke() ?? true))
            return false;

        entry.Invoke();
        return true;
    }

    private void Unregister(string id)
    {
        if (!_entries.Remove(id, out Entry? entry))
            return;
        _orderedEntries.Remove(entry);
        Changed?.Invoke();
    }

    private static int CompareEntries(Entry left, Entry right)
    {
        int byOrder = left.Definition.Order.CompareTo(right.Definition.Order);
        return byOrder != 0
            ? byOrder
            : StringComparer.Ordinal.Compare(left.Definition.Id, right.Definition.Id);
    }

    private sealed record Entry(
        CustomizeCommandDefinition Definition,
        Action Invoke,
        Func<bool>? IsVisible,
        Func<bool>? IsEnabled);

    private sealed class Registration : IDisposable
    {
        private CustomizeCommandRegistry? _owner;
        private readonly string _id;

        public Registration(CustomizeCommandRegistry owner, string id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            CustomizeCommandRegistry? owner = _owner;
            _owner = null;
            owner?.Unregister(_id);
        }
    }
}
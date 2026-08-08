using System;
using System.Collections.Generic;
using System.Linq;

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

        _entries.Add(definition.Id, new Entry(definition, invoke, isVisible, isEnabled));
        Changed?.Invoke();
        return new Registration(this, definition.Id);
    }

    public IReadOnlyList<CustomizeCommandSnapshot> Snapshot()
    {
        return _entries.Values
            .OrderBy(entry => entry.Definition.Order)
            .ThenBy(entry => entry.Definition.Id, StringComparer.Ordinal)
            .Select(entry => new CustomizeCommandSnapshot(
                entry.Definition,
                entry.IsVisible?.Invoke() ?? true,
                entry.IsEnabled?.Invoke() ?? true))
            .ToArray();
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
        if (!_entries.Remove(id))
            return;
        Changed?.Invoke();
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

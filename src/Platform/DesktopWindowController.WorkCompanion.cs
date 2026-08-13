using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Physics;
using DesktopBuddy.Domain.Platform;
using Godot;

namespace DesktopBuddy.Platform;

public partial class DesktopWindowController
{
    public static readonly Vector2I MinimumWorkCompanionSize = new(360, 215);

    private WindowSettings? _preWorkCompanionSettings;

    public bool WorkCompanionActive { get; private set; }
    public Rect2I WorkCompanionRect => _lastAppliedRect;

    /// <summary>
    /// Temporarily replaces the normal compact app window with a small transparent,
    /// borderless Work companion. The normal compact bounds remain untouched and are
    /// restored exactly on exit.
    /// </summary>
    public void EnterWorkCompanionWindow(Rect2I requestedRect)
    {
        if (WorkCompanionActive)
        {
            MoveWorkCompanion(requestedRect.Position);
            return;
        }

        if (LayoutMode != WindowLayoutMode.Compact)
            TrySetLayoutMode(WindowLayoutMode.Compact, FullscreenMonitor);

        _preWorkCompanionSettings = _compactSettings;
        Rect2I recovered = RecoverWorkCompanionRect(requestedRect);
        WindowSettings workSettings = _compactSettings with
        {
            Rect = recovered,
            Transparent = true,
            Borderless = true,
            Resizable = true,
            AlwaysOnTop = true,
        };

        WorkCompanionActive = true;
        _suppressClientBoundsChanged = true;
        _lastAppliedRect = recovered;
        _lastAppliedSettings = workSettings;
        TransparencyActive = _adapter.TransparencyAvailable;

        if (!_headless)
        {
            Window window = GetWindow();
            window.Mode = Window.ModeEnum.Windowed;
            // Work Mode intentionally uses a much smaller footprint than the playable room.
            // The normal room minimum is restored in ExitWorkCompanionWindow.
            window.MinSize = MinimumWorkCompanionSize;
            window.Borderless = true;
            window.Unresizable = false;
            window.AlwaysOnTop = true;
            window.Transparent = TransparencyActive;
            GetViewport().TransparentBg = TransparencyActive;
            window.Size = recovered.Size;
            window.Position = recovered.Position;
        }

        ApplyCurrentInputPolicy();
        _suppressClientBoundsChanged = false;
        ClientBoundsChanged?.Invoke(recovered);
    }

    public void MoveWorkCompanion(Vector2I requestedPosition)
    {
        if (!WorkCompanionActive)
            return;
        Rect2I recovered = RecoverWorkCompanionRect(
            new Rect2I(requestedPosition, _lastAppliedRect.Size));
        _lastAppliedRect = recovered;
        _lastAppliedSettings = _lastAppliedSettings with { Rect = recovered };
        if (!_headless)
            GetWindow().Position = recovered.Position;
    }

    public void ResizeWorkCompanion(Vector2I requestedSize)
        => ResizeWorkCompanion(requestedSize, null);

    public void ResizeWorkCompanion(Vector2I requestedSize, Vector2? normalizedAnchor)
    {
        if (!WorkCompanionActive)
            return;
        Vector2I requestedPosition = _lastAppliedRect.Position;
        if (normalizedAnchor is Vector2 anchor && anchor.IsFinite())
        {
            anchor = anchor.Clamp(Vector2.Zero, Vector2.One);
            Vector2 desktopAnchor = (Vector2)_lastAppliedRect.Position +
                ((Vector2)_lastAppliedRect.Size * anchor);
            Vector2 anchoredPosition = desktopAnchor - ((Vector2)requestedSize * anchor);
            requestedPosition = new Vector2I(
                Mathf.RoundToInt(anchoredPosition.X),
                Mathf.RoundToInt(anchoredPosition.Y));
        }
        Rect2I recovered = RecoverWorkCompanionRect(
            new Rect2I(requestedPosition, requestedSize));
        _lastAppliedRect = recovered;
        _lastAppliedSettings = _lastAppliedSettings with { Rect = recovered };
        if (!_headless)
        {
            Window window = GetWindow();
            window.Size = recovered.Size;
            window.Position = recovered.Position;
        }
        ClientBoundsChanged?.Invoke(recovered);
    }

    public void StartWorkCompanionResize()
    {
        if (WorkCompanionActive && !_headless)
            GetWindow().StartResize(DisplayServer.WindowResizeEdge.BottomRight);
    }

    public void ExitWorkCompanionWindow()
    {
        if (!WorkCompanionActive)
            return;

        WindowSettings restore = RecoverWindowSettings(
            _preWorkCompanionSettings ?? _compactSettings);
        _preWorkCompanionSettings = null;
        WorkCompanionActive = false;
        _compactSettings = restore;
        _lastAppliedSettings = restore;
        _lastAppliedRect = restore.Rect;
        TransparencyActive = restore.Transparent && _adapter.TransparencyAvailable;
        _suppressClientBoundsChanged = true;

        if (!_headless)
        {
            Window window = GetWindow();
            window.Mode = Window.ModeEnum.Windowed;
            window.MinSize = CompactMinimumSize;
            window.Borderless = restore.Borderless;
            window.Unresizable = !restore.Resizable;
            window.AlwaysOnTop = restore.AlwaysOnTop;
            window.Transparent = TransparencyActive;
            GetViewport().TransparentBg = TransparencyActive;
            window.Size = restore.Rect.Size;
            window.Position = restore.Rect.Position;
            ApplyRenderSettings(restore);
        }

        ApplyCurrentInputPolicy();
        _suppressClientBoundsChanged = false;
        ClientBoundsChanged?.Invoke(restore.Rect);
    }

    /// <summary>
    /// Resolves a Work window against usable monitor work areas without applying the normal
    /// gameplay-room minimum size. At least a small grab strip remains on-screen.
    /// </summary>
    public Rect2I RecoverWorkCompanionRect(Rect2I requested)
    {
        IReadOnlyList<Rect2I> monitors = _adapter.GetUsableMonitorRects();
        if (monitors.Count == 0)
            return requested;

        Rect2I target = monitors[0];
        long bestArea = -1;
        foreach (Rect2I monitor in monitors)
        {
            Rect2I intersection = requested.Intersection(monitor);
            long area = (long)Math.Max(0, intersection.Size.X) * Math.Max(0, intersection.Size.Y);
            if (area > bestArea)
            {
                bestArea = area;
                target = monitor;
            }
        }

        Vector2I size = new(
            Math.Clamp(requested.Size.X, MinimumWorkCompanionSize.X,
                Math.Max(MinimumWorkCompanionSize.X, target.Size.X)),
            Math.Clamp(requested.Size.Y, MinimumWorkCompanionSize.Y,
                Math.Max(MinimumWorkCompanionSize.Y, target.Size.Y)));
        const int recoverable = 48;
        int minX = target.Position.X - size.X + recoverable;
        int maxX = target.End.X - recoverable;
        int minY = target.Position.Y;
        int maxY = target.End.Y - recoverable;
        return new Rect2I(
            new Vector2I(
                Math.Clamp(requested.Position.X, minX, maxX),
                Math.Clamp(requested.Position.Y, minY, maxY)),
            size);
    }

    public Rect2I DefaultWorkCompanionRect(Vector2I size, int inset = 12)
    {
        Rect2I usable = UsableMonitorRect;
        return RecoverWorkCompanionRect(new Rect2I(
            new Vector2I(
                usable.End.X - size.X - inset,
                usable.End.Y - size.Y - inset),
            size));
    }
}

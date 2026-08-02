using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Native Windows monitor/DPI/lifecycle adapter. Full-screen Work passthrough is now owned by
/// Godot's native <see cref="Window.MousePassthrough"/> flag and a separate toolbar window.
/// The legacy region-based WM_NCHITTEST seam remains lazy for compatibility with older tests,
/// but normal production composition never activates or subclasses the Godot window.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopAdapter : IWindowsDesktopAdapter
{
    private const string Category = "WinAdapter";

    private const int GwlpWndProc = -4;
    private const uint WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const int SwHide = 0;
    private const int SwRestore = 9;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const int MdtEffectiveDpi = 0;

    private readonly IntPtr _hwnd;
    private readonly List<Rect2I> _monitorRects = new();
    private readonly List<IntPtr> _monitorHandles = new();

    private WndProc? _hookDelegate;
    private IntPtr _originalWndProc = IntPtr.Zero;
    private bool _subclassed;

    private Rect2I[] _hitRegions = new Rect2I[16];
    private int _hitRegionCount;
    private bool _workModeActive;

    public bool IsNative => true;
    public bool TransparencyAvailable { get; }
    public bool IsWindowVisible => IsWindowVisibleNative(_hwnd);

#pragma warning disable CS0067
    public event Action? SystemSuspending;
    public event Action? SystemResumed;
    public event Action<bool>? SessionLockChanged;
#pragma warning restore CS0067

    public WindowsDesktopAdapter()
    {
        long handle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        _hwnd = new IntPtr(handle);

        bool allowed = (bool)ProjectSettings.GetSetting(
            "display/window/per_pixel_transparency/allowed",
            false);
        TransparencyAvailable = allowed && DwmIsCompositionEnabledSafe();

        RefreshMonitors();
        Log.Info(Category,
            $"Native adapter attached without WndProc subclassing " +
            $"(hwnd=0x{handle:X} monitors={_monitorRects.Count} " +
            $"transparency={TransparencyAvailable}).");
    }

    public IReadOnlyList<Rect2I> GetUsableMonitorRects()
    {
        RefreshMonitors();
        return _monitorRects;
    }

    public float GetDpiScale(int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= _monitorHandles.Count)
            return 1.0f;

        if (GetDpiForMonitor(
                _monitorHandles[monitorIndex],
                MdtEffectiveDpi,
                out uint dpiX,
                out _) == 0)
        {
            return dpiX / 96.0f;
        }

        return 1.0f;
    }

    /// <summary>
    /// Legacy-only selective hit-testing seam. Production no longer calls this because
    /// HTTRANSPARENT does not reliably forward to arbitrary applications. Subclassing is
    /// therefore delayed until an explicit legacy caller requests it.
    /// </summary>
    public void SetWorkModeHitRegions(IReadOnlyList<Rect2I> regions)
    {
        ArgumentNullException.ThrowIfNull(regions);
        Subclass();
        if (!_subclassed)
            return;

        if (regions.Count > _hitRegions.Length)
            Array.Resize(ref _hitRegions, regions.Count);
        for (int index = 0; index < regions.Count; index++)
            _hitRegions[index] = regions[index];
        _hitRegionCount = regions.Count;
        _workModeActive = true;
    }

    public void SetPlayModeCapture() => _workModeActive = false;

    public void SetWindowVisible(bool visible)
    {
        if (_hwnd == IntPtr.Zero)
        {
            Log.Error(Category, "Cannot change main-window visibility without a valid HWND.");
            return;
        }

        ShowWindow(_hwnd, visible ? SwRestore : SwHide);
        if (IsWindowVisibleNative(_hwnd) != visible)
            Log.Error(Category, $"Native main-window visibility did not become {visible}.");
    }

    public void Shutdown()
    {
        if (_subclassed && _originalWndProc != IntPtr.Zero)
        {
            SetWindowLongPtr(_hwnd, GwlpWndProc, _originalWndProc);
            _subclassed = false;
            _originalWndProc = IntPtr.Zero;
        }

        _hookDelegate = null;
        Log.Info(Category, "Native adapter shut down.");
    }

    private void Subclass()
    {
        if (_subclassed || _hwnd == IntPtr.Zero)
            return;

        _hookDelegate = HookProc;
        IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(_hookDelegate);
        _originalWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc, hookPtr);
        _subclassed = _originalWndProc != IntPtr.Zero;
        if (!_subclassed)
            Log.Error(Category, "Failed to install legacy region hit-test hook.");
    }

    private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmNcHitTest && _workModeActive)
        {
            long packed = lParam.ToInt64();
            var point = new POINT
            {
                X = unchecked((short)(packed & 0xFFFF)),
                Y = unchecked((short)((packed >> 16) & 0xFFFF)),
            };
            if (ScreenToClient(hWnd, ref point))
            {
                bool inside = PointInAnyRegion(point.X, point.Y);
                return new IntPtr(inside ? HtClient : HtTransparent);
            }
        }

        return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private bool PointInAnyRegion(int x, int y)
    {
        for (int index = 0; index < _hitRegionCount; index++)
        {
            Rect2I region = _hitRegions[index];
            if (x >= region.Position.X && x < region.Position.X + region.Size.X &&
                y >= region.Position.Y && y < region.Position.Y + region.Size.Y)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshMonitors()
    {
        _monitorRects.Clear();
        _monitorHandles.Clear();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, MonitorEnumCallback, IntPtr.Zero);

        if (_monitorRects.Count == 0)
        {
            IntPtr primary = MonitorFromWindow(_hwnd, MonitorDefaultToPrimary);
            if (TryGetWorkArea(primary, out Rect2I rect))
            {
                _monitorRects.Add(rect);
                _monitorHandles.Add(primary);
            }
        }
    }

    private bool MonitorEnumCallback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
    {
        if (TryGetWorkArea(hMonitor, out Rect2I work))
        {
            _monitorRects.Add(work);
            _monitorHandles.Add(hMonitor);
        }

        return true;
    }

    private static bool TryGetWorkArea(IntPtr hMonitor, out Rect2I rect)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(hMonitor, ref info))
        {
            RECT w = info.rcWork;
            rect = new Rect2I(w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top);
            return true;
        }

        rect = default;
        return false;
    }

    private static bool DwmIsCompositionEnabledSafe()
    {
        try
        {
            return DwmIsCompositionEnabled(out bool enabled) == 0 && enabled;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate bool MonitorEnumProc(
        IntPtr hMonitor,
        IntPtr hdc,
        ref RECT rect,
        IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static extern IntPtr CallWindowProc(
        IntPtr lpPrevWndFunc,
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisibleNative(IntPtr hWnd);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr hMonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled(
        [MarshalAs(UnmanagedType.Bool)] out bool enabled);
}

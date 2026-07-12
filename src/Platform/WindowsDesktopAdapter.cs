using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// Native Windows implementation of <see cref="IWindowsDesktopAdapter"/>
/// (`ARCHITECTURE.md` §9). It obtains the real HWND from Godot, subclasses the
/// window procedure to answer <c>WM_NCHITTEST</c> with <c>HTTRANSPARENT</c> over
/// transparent pixels in Work Mode (so clicks fall through to the desktop) and
/// <c>HTCLIENT</c> over the interactive regions, enumerates usable monitor work
/// areas, and reports per-monitor DPI. It never uses <c>SetWindowRgn</c> (that
/// clips presentation as well as input); it restores the original procedure on
/// <see cref="Shutdown"/>.
///
/// UNVERIFIED SKELETON: this must be exercised on real Windows 10/11 hardware
/// (`TEST_PLAN.md` §5) — CI and headless use the emulated adapter, so a defect
/// here cannot affect the automated gates. Tray icon, global hotkey,
/// launch-at-login, and the §24 lifecycle messages extend this in a follow-up
/// slice; they are not part of the current seam surface.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopAdapter : IWindowsDesktopAdapter
{
    private const string Category = "WinAdapter";

    private const int GwlpWndProc = -4;
    private const uint WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private const int HtClient = 1;
    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const int MdtEffectiveDpi = 0;

    private readonly IntPtr _hwnd;
    private readonly List<Rect2I> _monitorRects = new();
    private readonly List<IntPtr> _monitorHandles = new();

    // Keep the delegate rooted for the lifetime of the subclass, or the GC will
    // collect it and the window procedure pointer will dangle.
    private WndProc? _hookDelegate;
    private IntPtr _originalWndProc = IntPtr.Zero;
    private bool _subclassed;

    private List<Rect2I> _hitRegions = new();
    private bool _workModeActive;

    public bool IsNative => true;
    public bool TransparencyAvailable { get; }

    public WindowsDesktopAdapter()
    {
        long handle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        _hwnd = new IntPtr(handle);

        bool allowed = (bool)ProjectSettings.GetSetting("display/window/per_pixel_transparency/allowed", false);
        TransparencyAvailable = allowed && DwmIsCompositionEnabledSafe();

        RefreshMonitors();
        Subclass();
        Log.Info(Category, $"Native adapter attached (hwnd=0x{handle:X} monitors={_monitorRects.Count} transparency={TransparencyAvailable}).");
    }

    public IReadOnlyList<Rect2I> GetUsableMonitorRects()
    {
        RefreshMonitors();
        return _monitorRects;
    }

    public float GetDpiScale(int monitorIndex)
    {
        if (monitorIndex < 0 || monitorIndex >= _monitorHandles.Count)
        {
            return 1.0f;
        }

        if (GetDpiForMonitor(_monitorHandles[monitorIndex], MdtEffectiveDpi, out uint dpiX, out _) == 0)
        {
            return dpiX / 96.0f;
        }

        return 1.0f;
    }

    public void SetWorkModeHitRegions(IReadOnlyList<Rect2I> regions)
    {
        // Regions are treated as client-pixel rects. The shell currently supplies
        // sandbox-space rects; the sandbox→client mapping lands with the
        // InputCollector coordinate layer (`ARCHITECTURE.md` §10). Until then this
        // is 1:1 only at 100% zoom with the camera at the client origin.
        _hitRegions = new List<Rect2I>(regions);
        _workModeActive = true;
    }

    public void SetPlayModeCapture()
    {
        _workModeActive = false;
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
        Log.Info(Category, "Native adapter restored window procedure.");
    }

    private void Subclass()
    {
        if (_subclassed || _hwnd == IntPtr.Zero)
        {
            return;
        }

        _hookDelegate = HookProc;
        IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(_hookDelegate);
        _originalWndProc = SetWindowLongPtr(_hwnd, GwlpWndProc, hookPtr);
        _subclassed = _originalWndProc != IntPtr.Zero;
        if (!_subclassed)
        {
            Log.Error(Category, "Failed to subclass the window procedure; Work-Mode passthrough is unavailable.");
        }
    }

    private IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmNcHitTest && _workModeActive)
        {
            long packed = lParam.ToInt64();
            var point = new POINT { X = unchecked((short)(packed & 0xFFFF)), Y = unchecked((short)((packed >> 16) & 0xFFFF)) };
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
        foreach (Rect2I region in _hitRegions)
        {
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
            // Fall back to the primary monitor's work area if enumeration failed.
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

    // ---- Win32 interop ----

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

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
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
}

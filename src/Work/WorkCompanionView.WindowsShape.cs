using System;
using System.Runtime.InteropServices;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Work;

/// <summary>
/// Gives the borderless Work window a native Windows region matching the visible companion
/// composition. Pixels outside these coarse art regions are not part of the HWND at all, so
/// clicks pass to the application underneath instead of being swallowed by a transparent box.
/// It also temporarily suspends the normal Win98 shell backdrop so Work stays truly transparent.
/// </summary>
public partial class WorkCompanionView
{
    private const int RgnOr = 2;
    private nint _ownedWorkWindowHandle;
    private bool _nativeShapeApplied;
    private bool _nativeShapeRefreshPending;
    private double _nativeShapeStableSeconds;
    private Win98BuddyShellController? _normalWin98Shell;
    private WorldEnvironment? _normalBackdrop;
    private bool _normalShellWasProcessing;
    private bool _normalFrameWasVisible;
    private Godot.Environment? _normalBackdropEnvironment;
    private bool _normalShellIsolated;

    public override void _EnterTree()
    {
        IsolateNormalShell();
        EnsureRewardOverlay();
        ApplyNativeWindowShape();
    }

    public override void _ExitTree()
    {
        _nativeShapeRefreshPending = false;
        ClearNativeWindowShape();
        RestoreNormalShell();
    }

    private void IsolateNormalShell()
    {
        _normalWin98Shell = GetTree().Root.FindChild(
            nameof(Win98BuddyShellController), true, false) as Win98BuddyShellController;
        if (GodotObject.IsInstanceValid(_normalWin98Shell))
        {
            _normalShellWasProcessing = _normalWin98Shell!.IsProcessing();
            _normalFrameWasVisible = _normalWin98Shell.Frame.Visible;
            _normalWin98Shell.Frame.Visible = false;
            _normalWin98Shell.SetProcess(false);
        }

        _normalBackdrop = GetTree().Root.FindChild(
            "Win98BackdropEnvironment", true, false) as WorldEnvironment;
        // Detach the environment rather than fading its colour: a BGMode.Color background
        // still clears the frame opaquely regardless of its alpha, which left the Win98
        // face grey showing through every part of the shaped Work window.
        if (GodotObject.IsInstanceValid(_normalBackdrop) &&
            GodotObject.IsInstanceValid(_normalBackdrop!.Environment))
        {
            _normalBackdropEnvironment = _normalBackdrop.Environment;
            _normalBackdrop.Environment = null;
        }

        if (DisplayServer.GetName() != "headless")
        {
            GetWindow().Transparent = true;
            GetViewport().TransparentBg = true;
        }
        _normalShellIsolated = true;
    }

    private void RestoreNormalShell()
    {
        if (!_normalShellIsolated)
            return;

        if (GodotObject.IsInstanceValid(_normalBackdrop) &&
            GodotObject.IsInstanceValid(_normalBackdropEnvironment))
        {
            _normalBackdrop!.Environment = _normalBackdropEnvironment;
        }
        _normalBackdropEnvironment = null;
        if (GodotObject.IsInstanceValid(_normalWin98Shell))
        {
            _normalWin98Shell!.Frame.Visible = _normalFrameWasVisible;
            _normalWin98Shell!.SetProcess(_normalShellWasProcessing);
        }

        _normalBackdrop = null;
        _normalWin98Shell = null;
        _normalShellIsolated = false;
    }

    private void ApplyNativeWindowShape()
    {
        if (!OperatingSystem.IsWindows() || DisplayServer.GetName() == "headless")
            return;

        long rawHandle = DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle);
        if (rawHandle == 0)
            return;

        _ownedWorkWindowHandle = new nint(rawHandle);
        nint combined = CreateRectRgn(0, 0, 0, 0);
        if (combined == 0)
            return;

        // Match the scaled sideways-buddy and supplied-PC layout. The rectangles overlap enough
        // for tiny hand excursions while empty corners stay outside the HWND and click through.
        Rect2I[] regions =
        [
            new Rect2I(228, 78, 152, 228),   // sideways buddy + alternating typing hands
            new Rect2I(385, 68, 240, 270),   // smaller supplied monitor and PC chassis
        ];

        // The hover controls are not part of the composition and do not scale with it, so their
        // slice of the HWND is measured in window pixels and added on its own. Leaving it out
        // would clip the buttons straight out of the clickable window.
        bool built = AddNativeRegion(combined, ControlClusterWindowRect());
        foreach (Rect2I unscaled in regions)
        {
            if (!built)
                break;

            Rect2I region = ScaleCompositionRect(unscaled);
            built = AddNativeRegion(combined, region);
        }

        if (!built || SetWindowRgn(_ownedWorkWindowHandle, combined, true) == 0)
        {
            DeleteObject(combined);
            _ownedWorkWindowHandle = 0;
            return;
        }

        // After a successful SetWindowRgn Windows owns the HRGN handle.
        _nativeShapeApplied = true;
    }

    private static bool AddNativeRegion(nint combined, Rect2I region)
    {
        nint part = CreateRectRgn(
            region.Position.X,
            region.Position.Y,
            region.End.X,
            region.End.Y);
        if (part == 0)
            return false;

        int result = CombineRgn(combined, combined, part, RgnOr);
        DeleteObject(part);
        return result != 0;
    }

    private void ScheduleNativeWindowShapeRefresh()
    {
        if (!OperatingSystem.IsWindows() || DisplayServer.GetName() == "headless")
            return;
        _nativeShapeRefreshPending = true;
        _nativeShapeStableSeconds = 0.0;
        if (_nativeShapeApplied)
            ClearNativeWindowShape();
    }

    private void TickNativeWindowShape(double delta)
    {
        if (!_nativeShapeRefreshPending)
            return;
        _nativeShapeStableSeconds += Math.Max(0.0, delta);
        if (_nativeShapeStableSeconds < 0.08)
            return;
        _nativeShapeRefreshPending = false;
        ApplyNativeWindowShape();
    }

    private void ClearNativeWindowShape()
    {
        if (!_nativeShapeApplied || _ownedWorkWindowHandle == 0 || !OperatingSystem.IsWindows())
            return;

        SetWindowRgn(_ownedWorkWindowHandle, 0, true);
        _nativeShapeApplied = false;
        _ownedWorkWindowHandle = 0;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int CombineRgn(nint destination, nint source1, nint source2, int combineMode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint hWnd, nint hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}

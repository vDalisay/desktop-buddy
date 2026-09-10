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
    private ulong _nativeShapeScheduledFrame;
    private Win98BuddyShellController? _normalWin98Shell;
    private WorldEnvironment? _normalBackdrop;
    private bool _normalShellWasProcessing;
    private bool _normalFrameWasVisible;
    private Godot.Environment? _normalBackdropEnvironment;
    private Color _normalClearColor;
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
            // The engine's default clear colour is opaque grey, and a resized window clears
            // with it before the transparent frame at the new size lands. That grey - not the
            // window shape - is the smear that flashed across the companion while resizing
            // (owner report 2026-09-10). Clearing to nothing makes those frames invisible.
            _normalClearColor = RenderingServer.GetDefaultClearColor();
            RenderingServer.SetDefaultClearColor(new Color(0.0f, 0.0f, 0.0f, 0.0f));
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
        if (DisplayServer.GetName() != "headless")
            RenderingServer.SetDefaultClearColor(_normalClearColor);
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
            new Rect2I(393, 71, 259, 259),   // pixel-art monitor, chassis and its drop shadow
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

        // Godot redraws the transparent client every frame. A native redraw here repaints
        // newly exposed region strips before that frame, producing grey resize outlines.
        if (!built || SetWindowRgn(_ownedWorkWindowHandle, combined, false) == 0)
        {
            // Keep whatever region already succeeded, handle included: forgetting the handle
            // here would leave a failed re-shape stuck on the last good region with nothing to
            // clear it on the way out of Work Mode.
            DeleteObject(combined);
            if (!_nativeShapeApplied)
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

    /// <summary>
    /// Re-shapes the window to the size it has right now.
    ///
    /// <para>The shape used to be dropped here and rebuilt 80 ms later, which put the whole
    /// rectangular HWND back on screen for the gap. Resizing changes the size continuously, so
    /// the gap never closed and the bare window - which Windows paints with its own grey
    /// background - flashed for the entire gesture (owner report 2026-09-10).</para>
    ///
    /// <para>It applies immediately rather than a frame late: lagging the shape behind the
    /// window only traded the flash for the art being clipped along the drag. The undrawn
    /// pixels a growing region exposes are handled where they come from - the clear colour, in
    /// IsolateNormalShell - not by withholding the shape.</para>
    /// </summary>
    private void ScheduleNativeWindowShapeRefresh()
    {
        if (!OperatingSystem.IsWindows() || DisplayServer.GetName() == "headless")
            return;
        _nativeShapeRefreshPending = true;
        _nativeShapeScheduledFrame = Engine.GetProcessFrames();
        ApplyNativeWindowShape();
    }

    private void TickNativeWindowShape()
    {
        if (!_nativeShapeRefreshPending ||
            Engine.GetProcessFrames() <= _nativeShapeScheduledFrame)
        {
            return;
        }

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

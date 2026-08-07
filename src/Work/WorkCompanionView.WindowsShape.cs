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
    private Win98BuddyShellController? _normalWin98Shell;
    private WorldEnvironment? _normalBackdrop;
    private bool _normalShellWasProcessing;
    private Color _normalBackdropColor;
    private bool _normalShellIsolated;

    public override void _EnterTree()
    {
        IsolateNormalShell();
        ApplyNativeWindowShape();
    }

    public override void _ExitTree()
    {
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
            _normalWin98Shell.SetProcess(false);
        }

        _normalBackdrop = GetTree().Root.FindChild(
            "Win98BackdropEnvironment", true, false) as WorldEnvironment;
        if (GodotObject.IsInstanceValid(_normalBackdrop) &&
            GodotObject.IsInstanceValid(_normalBackdrop!.Environment))
        {
            _normalBackdropColor = _normalBackdrop.Environment.BackgroundColor;
            _normalBackdrop.Environment.BackgroundColor = new Color(
                _normalBackdropColor.R,
                _normalBackdropColor.G,
                _normalBackdropColor.B,
                0.0f);
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
            GodotObject.IsInstanceValid(_normalBackdrop!.Environment))
        {
            _normalBackdrop.Environment.BackgroundColor = _normalBackdropColor;
        }
        if (GodotObject.IsInstanceValid(_normalWin98Shell))
            _normalWin98Shell!.SetProcess(_normalShellWasProcessing);

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

        // Coarse unions deliberately cover visible art plus a few animation pixels. The shape
        // can be refined with authored alpha contours later without changing input semantics.
        Rect2I[] regions =
        [
            new Rect2I(8, 8, 102, 34),       // hover motion control
            new Rect2I(4, 24, 255, 230),     // buddy
            new Rect2I(349, 30, 178, 205),   // CRT/monitor + case
            new Rect2I(164, 218, 184, 44),   // keyboard
            new Rect2I(108, 224, 58, 44),    // mouse/cable interaction area
            new Rect2I(14, 233, 532, 56),    // desk
        ];

        bool built = true;
        foreach (Rect2I region in regions)
        {
            nint part = CreateRectRgn(
                region.Position.X,
                region.Position.Y,
                region.End.X,
                region.End.Y);
            if (part == 0)
            {
                built = false;
                break;
            }

            int result = CombineRgn(combined, combined, part, RgnOr);
            DeleteObject(part);
            if (result == 0)
            {
                built = false;
                break;
            }
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

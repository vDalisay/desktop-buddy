using System;
using System.Runtime.InteropServices;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Ui;

/// <summary>
/// A free-floating desktop window for one dock panel. Each is a native transient child of the
/// main window: Windows keeps an owned window above its owner, including above a non-exclusive
/// full-screen parent, without any topmost flag. AlwaysOnTop must stay false — Godot documents
/// that it does not work on transient windows, and toggling it to force restacking produced the
/// focus contest this class used to carry. Closing hides the window; the panel is retained.
/// </summary>
public partial class DockWindow : Window
{
    private const string DiagnosticsCategory = "DockWindow";

    public int OpenCount { get; private set; }

    /// <summary>Raised whenever the window is shown, so the panel can refresh first.</summary>
    public event Action? Opening;

    public void Configure(string title, Vector2I size, Control content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Title = title;
        Name = title.Replace(" ", string.Empty) + "Window";
        Size = size;
        MinSize = size;
        Unresizable = false;
        Visible = false;

        ApplyOwnedWindowFlags(this);

        content.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(content);
        CloseRequested += Hide;
    }

    /// <summary>
    /// The one supported flag set for every native window this game owns. Transient makes the
    /// window a child of the window its node lives in, which is what keeps it above the overlay.
    /// </summary>
    public static void ApplyOwnedWindowFlags(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        // No ForceNative: display/window/subwindows/embed_subwindows is already false, so every
        // subwindow is native. Setting it creates the HWND at tree-entry, before Godot can bind
        // the transient owner, which left these windows owner-less and z-ordered by activation.
        window.Transient = true;
        window.TransientToFocused = false;
        window.Exclusive = false;
        window.AlwaysOnTop = false;
        window.Unfocusable = false;
        window.MousePassthrough = false;
        // The gameplay tree pauses for tray/editor transitions; owned windows keep working.
        window.ProcessMode = ProcessModeEnum.Always;
    }

    /// <summary>Shows a registered owned window and asks for focus once, on the next frame.</summary>
    public static void ShowOwned(Window window)
    {
        window.Show();
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(window) || !window.Visible)
                return;
            AdoptNativeOwner(window);
            window.GrabFocus();
        }).CallDeferred();
    }

    /// <summary>
    /// Makes the main window the native owner of this one. Godot's <c>Transient</c> flag records
    /// the relationship in the scene tree but did not reach Windows here — the child HWNDs came
    /// back with owner=0 and were therefore z-ordered by activation alone, which let the
    /// full-screen overlay sit above them and swallow every click aimed at their buttons. An
    /// owned window is unconditionally above its owner, so this is set once per show rather than
    /// restacked per frame.
    /// </summary>
    private static void AdoptNativeOwner(Window window)
    {
        if (OS.GetName() != "Windows")
            return;

        var child = (IntPtr)DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, window.GetWindowId());
        var main = (IntPtr)DisplayServer.WindowGetNativeHandle(
            DisplayServer.HandleType.WindowHandle, MainWindowId);
        if (child == IntPtr.Zero || main == IntPtr.Zero || child == main)
            return;

        IntPtr previous = GetWindowLongPtr(child, GwlpHwndParent);
        if (previous != main)
            SetWindowLongPtr(child, GwlpHwndParent, main);

        Log.Info(DiagnosticsCategory,
            $"[OwnedWindow] name={window.Name} windowId={window.GetWindowId()} " +
            $"hwnd=0x{child:X} owner=0x{main:X} previousOwner=0x{previous:X} " +
            $"transient={window.Transient} alwaysOnTop={window.AlwaysOnTop} " +
            $"exclusive={window.Exclusive} rect={new Rect2I(window.Position, window.Size)}.");
    }

    private const int MainWindowId = 0;
    private const int GwlpHwndParent = -8;

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    /// <summary>Show or hide the window, placing a first open near the game window.</summary>
    public void Toggle(Vector2I anchor)
    {
        if (Visible)
        {
            Hide();
            return;
        }

        if (OpenCount == 0)
            Position = anchor;
        OpenCount++;
        Opening?.Invoke();
        ShowOwned(this);
    }
}

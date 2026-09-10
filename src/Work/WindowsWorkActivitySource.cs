using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Work;

/// <summary>
/// Windows low-level hook adapter for Work Mode. It intentionally discards key identity
/// immediately after repeat suppression and never logs or persists raw input.
///
/// <para>The hooks live on a dedicated thread with its own message pump, never on Godot's main
/// thread. A WH_KEYBOARD_LL/WH_MOUSE_LL callback is delivered on the thread that installed it,
/// and Windows blocks the originating input event until that thread's pump dispatches it — so
/// hooks installed from the frame loop made every keystroke and mouse move on the whole desktop
/// wait behind a rendered frame, and dropped out entirely on a frame over LowLevelHooksTimeout
/// (owner report 2026-09-10). This thread does nothing but pump, so callbacks return in
/// microseconds regardless of the frame rate.</para>
/// </summary>
public sealed class WindowsWorkActivitySource : IWorkActivitySource
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;

    private const int WmQuit = 0x0012;

    private readonly object _sync = new();
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private nint _keyboardHook;
    private nint _mouseHook;
    private Thread? _pump;
    private uint _pumpThreadId;
    private WorkActivitySourceResult _startResult;
    private bool _disposed;

    public WindowsWorkActivitySource()
    {
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    public event Action<WorkActivityKind>? Activity;
    public bool IsRunning => _pump is { IsAlive: true } && _keyboardHook != 0 && _mouseHook != 0;

    public WorkActivitySourceResult Start()
    {
        ThrowIfDisposed();
        if (IsRunning)
            return WorkActivitySourceResult.Started;
        if (!OperatingSystem.IsWindows())
            return WorkActivitySourceResult.Failed("Global Work activity capture is only available on Windows.");

        Stop();
        // Deliberately not disposed: on the timeout path below the pump thread may still be
        // holding it, and disposing it out from under that thread is a crash, not a cleanup.
        var ready = new ManualResetEventSlim(false);
        _pump = new Thread(() => PumpHooks(ready))
        {
            IsBackground = true,
            Name = "WorkActivityHooks",
        };
        _pump.Start();
        // The hooks are installed on the pump thread, so entry has to wait for its verdict.
        // Bounded: a thread that cannot report in a second is a failure worth reporting.
        if (!ready.Wait(TimeSpan.FromSeconds(1.0)))
        {
            Stop();
            return WorkActivitySourceResult.Failed("Work activity hook thread did not start.");
        }

        if (!_startResult.Success)
            Stop();
        return _startResult;
    }

    /// <summary>
    /// Owns the hooks for their whole lifetime: Windows requires the installing thread to keep
    /// pumping messages, and only that thread may unhook them.
    /// </summary>
    private void PumpHooks(ManualResetEventSlim ready)
    {
        _pumpThreadId = GetCurrentThreadId();
        nint module = GetCurrentModuleHandle();
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        if (_keyboardHook == 0)
        {
            _startResult = WorkActivitySourceResult.Failed(
                $"Keyboard activity hook could not start (Win32 {Marshal.GetLastWin32Error()}).");
            ready.Set();
            return;
        }

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        if (_mouseHook == 0)
        {
            _startResult = WorkActivitySourceResult.Failed(
                $"Mouse activity hook could not start (Win32 {Marshal.GetLastWin32Error()}).");
            ready.Set();
            return;
        }

        _startResult = WorkActivitySourceResult.Started;
        ready.Set();

        while (GetMessage(out MSG message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        Unhook();
    }

    public void Stop()
    {
        Thread? pump = _pump;
        _pump = null;
        if (pump is not null)
        {
            if (_pumpThreadId != 0)
                PostThreadMessage(_pumpThreadId, WmQuit, 0, 0);
            if (!pump.Join(TimeSpan.FromSeconds(2.0)))
                Unhook();
            _pumpThreadId = 0;
        }
        else
        {
            Unhook();
        }

        lock (_sync)
            _pressedKeys.Clear();
    }

    private void Unhook()
    {
        if (_keyboardHook != 0)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
        if (_mouseHook != 0)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = 0;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private nint KeyboardHook(int code, nuint wParam, nint lParam)
    {
        if (code >= 0)
        {
            int message = unchecked((int)wParam);
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (message is WmKeyDown or WmSysKeyDown)
            {
                bool firstDown;
                lock (_sync)
                    firstDown = _pressedKeys.Add(data.VkCode);
                if (firstDown)
                    Activity?.Invoke(WorkActivityKind.KeyboardPress);
            }
            else if (message is WmKeyUp or WmSysKeyUp)
            {
                lock (_sync)
                    _pressedKeys.Remove(data.VkCode);
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private nint MouseHook(int code, nuint wParam, nint lParam)
    {
        if (code >= 0)
        {
            int message = unchecked((int)wParam);
            if (message is WmLButtonDown or WmRButtonDown or WmMButtonDown)
                Activity?.Invoke(WorkActivityKind.MouseClick);
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    private static nint GetCurrentModuleHandle()
    {
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        return module is null ? 0 : GetModuleHandle(module.ModuleName);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private delegate nint HookProc(int code, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nuint wParam, nint lParam);
}

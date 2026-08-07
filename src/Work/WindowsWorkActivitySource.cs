using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Work;

/// <summary>
/// Windows low-level hook adapter for Work Mode. It intentionally discards key identity
/// immediately after repeat suppression and never logs or persists raw input.
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

    private readonly object _sync = new();
    private readonly HashSet<uint> _pressedKeys = [];
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private nint _keyboardHook;
    private nint _mouseHook;
    private bool _disposed;

    public WindowsWorkActivitySource()
    {
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    public event Action<WorkActivityKind>? Activity;
    public bool IsRunning => _keyboardHook != 0 && _mouseHook != 0;

    public WorkActivitySourceResult Start()
    {
        ThrowIfDisposed();
        if (IsRunning)
            return WorkActivitySourceResult.Started;
        if (!OperatingSystem.IsWindows())
            return WorkActivitySourceResult.Failed("Global Work activity capture is only available on Windows.");

        Stop();
        nint module = GetCurrentModuleHandle();
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
        if (_keyboardHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Stop();
            return WorkActivitySourceResult.Failed($"Keyboard activity hook could not start (Win32 {error}).");
        }

        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);
        if (_mouseHook == 0)
        {
            int error = Marshal.GetLastWin32Error();
            Stop();
            return WorkActivitySourceResult.Failed($"Mouse activity hook could not start (Win32 {error}).");
        }

        return WorkActivitySourceResult.Started;
    }

    public void Stop()
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
        lock (_sync)
            _pressedKeys.Clear();
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
}

using System;
using System.Diagnostics;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.Platform;

/// <summary>
/// "Start with Windows", as the per-user Run key. Driven through <c>reg.exe</c> rather than the
/// registry API because the game targets plain <c>net8.0</c>, and one process call is cheaper
/// than a Windows-only package reference for a single toggle. Nothing outside this app's own
/// value under HKCU is ever read or written.
/// </summary>
public static class WindowsAutostart
{
    private const string Category = "Autostart";
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopBuddy";

    public static bool IsSupported => OS.GetName() == "Windows";

    public static bool IsEnabled()
    {
        if (!IsSupported)
            return false;
        return Run($"query \"{RunKey}\" /v {ValueName}", out string output) && output.Contains(ValueName, StringComparison.Ordinal);
    }

    /// <summary>Returns whether the requested state was reached.</summary>
    public static bool SetEnabled(bool enabled)
    {
        if (!IsSupported)
            return false;

        bool ok = enabled
            ? Run($"add \"{RunKey}\" /v {ValueName} /t REG_SZ /d \"{LaunchCommand()}\" /f", out _)
            : Run($"delete \"{RunKey}\" /v {ValueName} /f", out _);
        if (!ok)
            Log.Warn(Category, $"Could not {(enabled ? "add" : "remove")} the startup entry.");
        return ok;
    }

    /// <summary>
    /// From the editor the executable is Godot itself, so the project path has to travel with
    /// it or the entry would launch a bare editor at logon.
    /// </summary>
    private static string LaunchCommand()
    {
        string executable = OS.GetExecutablePath();
        return OS.HasFeature("editor")
            ? $"\\\"{executable}\\\" --path \\\"{ProjectSettings.GlobalizePath("res://")}\\\""
            : $"\\\"{executable}\\\"";
    }

    private static bool Run(string arguments, out string output)
    {
        output = string.Empty;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("reg.exe", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
                return false;

            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception exception)
        {
            Log.Warn(Category, $"Startup entry query failed: {exception.Message}");
            return false;
        }
    }
}

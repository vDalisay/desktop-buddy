using System;
using Godot;

namespace DesktopBuddy.Diagnostics;

/// <summary>Severity of a <see cref="Log"/> entry.</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Minimal structured logging for the game assembly. Every entry carries a
/// category and level and is emitted with a stable, machine-greppable prefix
/// (<c>[LEVEL] [Category] message</c>). Warnings/errors also route through
/// Godot's <see cref="GD.PushWarning(string)"/> / <see cref="GD.PushError(string)"/>
/// so they surface in the editor and in the MCP error log used for interactive
/// verification. This is intentionally tiny for Milestone 0; richer telemetry
/// (spring strain, tick time, etc.) arrives with the systems that produce it.
/// </summary>
public static class Log
{
    /// <summary>
    /// Optional sink invoked for every entry in addition to the Godot console,
    /// so the headless scenario/journey runners can capture logs into their
    /// artifact JSON without scraping stdout.
    /// </summary>
    public static event Action<LogLevel, string, string>? Sink;

    public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);

    public static void Info(string category, string message) => Write(LogLevel.Info, category, message);

    public static void Warn(string category, string message) => Write(LogLevel.Warn, category, message);

    public static void Error(string category, string message) => Write(LogLevel.Error, category, message);

    private static void Write(LogLevel level, string category, string message)
    {
        string line = $"[{level.ToString().ToUpperInvariant()}] [{category}] {message}";

        switch (level)
        {
            case LogLevel.Warn:
                GD.PushWarning(line);
                GD.Print(line);
                break;
            case LogLevel.Error:
                GD.PushError(line);
                GD.PrintErr(line);
                break;
            default:
                GD.Print(line);
                break;
        }

        Sink?.Invoke(level, category, message);
    }
}

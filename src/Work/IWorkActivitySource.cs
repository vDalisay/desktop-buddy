using System;
using DesktopBuddy.Domain.Work;

namespace DesktopBuddy.Work;

public readonly record struct WorkActivitySourceResult(bool Success, string? Detail = null)
{
    public static WorkActivitySourceResult Started { get; } = new(true);
    public static WorkActivitySourceResult Failed(string detail) => new(false, detail);
}

/// <summary>
/// Global activity boundary. Raw key identities never leave an implementation of this
/// interface; consumers receive only anonymous keyboard/click activity kinds.
/// </summary>
public interface IWorkActivitySource : IDisposable
{
    event Action<WorkActivityKind>? Activity;
    bool IsRunning { get; }
    WorkActivitySourceResult Start();
    void Stop();
}

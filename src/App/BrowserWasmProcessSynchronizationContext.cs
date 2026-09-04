using System;
using System.Collections.Generic;
using System.Threading;

namespace DesktopBuddy.App;

/// <summary>
/// Single-threaded browser-WASM continuation queue. The experimental Godot .NET Web runtime can
/// accept an async operation and then strand the captured continuation even though Godot keeps
/// rendering frames. Browser-only callers install this context after startup and drain it from a
/// ProcessMode.Always node, so ordinary async state machines can resume on the next rendered frame
/// without requiring Task.Run, a worker thread, or Godot's deferred callback queue.
/// </summary>
internal sealed class BrowserWasmProcessSynchronizationContext : SynchronizationContext
{
    private readonly Queue<WorkItem> _pending = new();

    public int PendingCount => _pending.Count;

    public override void Post(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _pending.Enqueue(new WorkItem(callback, state));
    }

    public override void Send(SendOrPostCallback callback, object? state)
    {
        ArgumentNullException.ThrowIfNull(callback);
        callback(state);
    }

    public void Install() => SetSynchronizationContext(this);

    public void Drain(int maximumCallbacks = 1024)
    {
        if (maximumCallbacks < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumCallbacks));

        int remaining = maximumCallbacks;
        while (_pending.Count > 0 && remaining-- > 0)
        {
            WorkItem item = _pending.Dequeue();
            Install();
            item.Callback(item.State);
        }

        // Keep the context installed after the process callback returns. Browser play has one
        // managed thread; later Godot signal callbacks in the same frame must capture this queue
        // rather than the experimental runtime context that stranded Paint Buddy persistence.
        Install();
    }

    private readonly record struct WorkItem(SendOrPostCallback Callback, object? State);
}

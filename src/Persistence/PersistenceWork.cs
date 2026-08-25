using System;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Persistence;

/// <summary>
/// Runs blocking persistence work on the thread pool for native builds, while executing it
/// inline for single-threaded browser-WASM builds where <see cref="Task.Run(Action)"/> has no
/// worker thread to make progress on. The returned Task still preserves the existing async API.
/// </summary>
internal static class PersistenceWork
{
    public static Task<T> Run<T>(Func<T> work, CancellationToken schedulingToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!OperatingSystem.IsBrowser())
            return Task.Run(work, schedulingToken);
        if (schedulingToken.IsCancellationRequested)
            return Task.FromCanceled<T>(schedulingToken);

        try
        {
            return Task.FromResult(work());
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            return Task.FromException<T>(exception);
        }
    }

    public static Task Run(Action work, CancellationToken schedulingToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!OperatingSystem.IsBrowser())
            return Task.Run(work, schedulingToken);
        if (schedulingToken.IsCancellationRequested)
            return Task.FromCanceled(schedulingToken);

        try
        {
            work();
            return Task.CompletedTask;
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }
}
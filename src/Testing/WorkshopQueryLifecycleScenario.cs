using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Platform.Steam;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Exercises the real adapter with an injected, silent callback source; no Steam account.</summary>
public sealed class WorkshopQueryLifecycleScenario : IScenario
{
    public string Id => "workshop_query_lifecycle";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        using var script = new GDScript { SourceCode = """
            extends Node
            signal workshop_item_created(result, file_id, legal)
            signal workshop_item_updated(result, legal, file_id)
            signal workshop_item_downloaded(result, app_id, file_id)
            signal workshop_query_completed(handle, result, count)
            signal query_completed(handle, result, count, total)
            var next_handle = 0
            var released = []
            func initialize(_app_id): return {"status": 0}
            func shutdown(): pass
            func get_item_state(_id): return 1
            func get_subscribed_items(): return PackedInt64Array([101])
            func query_item_details(_ids):
                next_handle += 1
                return next_handle
            func query_page(_sort, _page):
                next_handle += 1
                return next_handle
            func release_query(handle): released.append(handle)
            """ };
        if (script.Reload() != Error.Ok) throw new InvalidOperationException("Query fixture did not compile.");
        var bridge = (Node)script.New();
        var transport = new GodotSteamWorkshopTransport();
        tree.Root.AddChild(bridge);
        tree.Root.AddChild(transport);
        try
        {
            if (!transport.Initialize(bridge, 5114950)) throw new InvalidOperationException("Fixture initialization failed.");
            // Inject the optional discovery bridge at the same native boundary as Initialize.
            typeof(GodotSteamWorkshopTransport).GetField("_discoveryBridge", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(transport, bridge);
            MethodInfo browseCallback = typeof(GodotSteamWorkshopTransport).GetMethod(
                "OnBrowseQueryCompleted", BindingFlags.Instance | BindingFlags.NonPublic)!;
            bridge.Connect("query_completed", Callable.From<long, long, long, long>(
                (handle, result, count, total) => browseCallback.Invoke(transport, [handle, result, count, total])));

            ISteamWorkshopTransport api = transport;
            using var cancel = new CancellationTokenSource();
            Task<WorkshopSubscriptionQueryResult> first = api.GetItemDetailsAsync([101], cancel.Token);
            long firstHandle = bridge.Get("next_handle").AsInt64();
            WorkshopSubscriptionQueryResult other = await api.GetItemDetailsAsync([202], CancellationToken.None);
            WorkshopSubscriptionQueryResult subscriptions = await api.GetSubscribedItemsAsync(CancellationToken.None);
            Check("different_requests_never_share_results", other.Status == WorkshopRemoteStatus.Failed &&
                subscriptions.Status == WorkshopRemoteStatus.Failed && other.Items.Count == 0 && !first.IsCompleted);

            cancel.Cancel();
            WorkshopSubscriptionQueryResult cancelled = await first;
            Check("details_cancellation_releases_handle", cancelled.Status == WorkshopRemoteStatus.Cancelled && Releases(firstHandle) == 1);

            Task<WorkshopSubscriptionQueryResult> next = api.GetItemDetailsAsync([202], CancellationToken.None);
            long nextHandle = bridge.Get("next_handle").AsInt64();
            bridge.EmitSignal("workshop_query_completed", firstHandle, 1L, 0L);
            Check("late_details_callback_cannot_complete_new_owner", !next.IsCompleted);
            bridge.EmitSignal("workshop_query_completed", nextHandle, 1L, 0L);
            WorkshopSubscriptionQueryResult completed = await next;
            bridge.EmitSignal("workshop_query_completed", nextHandle, 1L, 0L);
            Check("details_completion_keeps_requested_identity_and_releases_once", completed.IsSuccess &&
                completed.Items.Count == 1 && completed.Items[0].PublishedFileId == 202 && Releases(nextHandle) == 1);

            using var browseCancel = new CancellationTokenSource();
            Task<WorkshopBrowsePage> browse = transport.BrowseAsync(WorkshopBrowseSort.MostRecent, 1, browseCancel.Token);
            long browseHandle = bridge.Get("next_handle").AsInt64();
            browseCancel.Cancel();
            Check("browse_cancellation_releases_handle", (await browse).Status == WorkshopRemoteStatus.Cancelled && Releases(browseHandle) == 1);

            Task<WorkshopBrowsePage> timedOut = transport.BrowseAsync(WorkshopBrowseSort.MostRecent, 2, CancellationToken.None);
            long timeoutHandle = bridge.Get("next_handle").AsInt64();
            WorkshopBrowsePage timeoutResult = await timedOut;
            Check("silent_browse_times_out_and_releases_handle", timeoutResult.Status == WorkshopRemoteStatus.Failed && Releases(timeoutHandle) == 1);

            Task<WorkshopBrowsePage> afterTimeout = transport.BrowseAsync(WorkshopBrowseSort.MostRecent, 3, CancellationToken.None);
            long finalHandle = bridge.Get("next_handle").AsInt64();
            bridge.EmitSignal("query_completed", timeoutHandle, 1L, 0L, 0L);
            Check("late_browse_callback_cannot_complete_new_owner", !afterTimeout.IsCompleted);
            tree.Root.RemoveChild(transport);
            Check("shutdown_completes_browse_and_releases_once", (await afterTimeout).Status == WorkshopRemoteStatus.Unavailable && Releases(finalHandle) == 1);
        }
        finally
        {
            if (transport.IsInsideTree()) tree.Root.RemoveChild(transport);
            transport.Free();
            bridge.Free();
        }
        return new ScenarioResult(checks.All(c => c.Passed), checks, [$"seed={seed}"]);

        int Releases(long handle) => bridge.Get("released").AsGodotArray().Count(value => value.AsInt64() == handle);
        void Check(string name, bool passed) => checks.Add(new StartupCheck(name, passed, passed ? "verified" : "failed"));
    }
}

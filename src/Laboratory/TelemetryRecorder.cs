using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using DesktopBuddy.Buddy;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Telemetry;
using DesktopBuddy.Grab;
using Godot;

namespace DesktopBuddy.Laboratory;

/// <summary>Debug-only streaming fixed-tick telemetry recorder.</summary>
public sealed partial class TelemetryRecorder : Node
{
    private const int PoolSize = 512;
    private readonly ConcurrentQueue<TelemetryFrame> _available = new();
    private readonly BlockingCollection<TelemetryFrame> _pending = new(new ConcurrentQueue<TelemetryFrame>());
    private BuddyRoot _buddy = null!;
    private GrabTetherController? _grab;
    private Thread? _writer;
    private string _jsonlPath = "";
    private string _envelopePath = "";
    private int _completed;
    private string[] _linkIds = Array.Empty<string>();

    public string JsonLinesPath => _jsonlPath;
    public string EnvelopePath => _envelopePath;

    public void Initialize(BuddyRoot buddy, GrabTetherController? grab, string artifactsDirectory, string id)
    {
        if (!OS.IsDebugBuild()) throw new InvalidOperationException("Telemetry recording is debug-only.");
        _buddy = buddy; _grab = grab;
        Directory.CreateDirectory(artifactsDirectory);
        _jsonlPath = Path.Combine(artifactsDirectory, $"telemetry_{id}.jsonl");
        _envelopePath = Path.Combine(artifactsDirectory, $"envelope_{id}.json");
        int links = buddy.Constraints.Telemetry.Count;
        _linkIds = new string[links];
        for (int i = 0; i < links; i++)
            _linkIds[i] = buddy.Constraints.Telemetry[i].LinkId.ToString();
        for (int i = 0; i < PoolSize; i++) _available.Enqueue(new TelemetryFrame(6, links));
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "TelemetryWriter" };
        _writer.Start();
    }

    public void Capture(long tick)
    {
        if (Volatile.Read(ref _completed) != 0) return;
        if (!_available.TryDequeue(out TelemetryFrame? frame)) return;
        frame.Tick = tick;
        for (int i = 0; i < frame.Parts.Length; i++)
        {
            PuppetPartBody part = _buddy.Rig.Parts[i];
            frame.Parts[i] = new PartTelemetry(i, part.GlobalPosition.X, part.GlobalPosition.Y,
                part.LinearVelocity.Length(), MathF.Abs(part.AngularVelocity));
        }
        for (int i = 0; i < frame.Links.Length; i++)
        {
            LinkTelemetry link = _buddy.Constraints.Telemetry[i];
            frame.Links[i] = new LinkTelemetrySample(_linkIds[i], link.Separation, link.Strain);
        }
        StandingSnapshot standing = _buddy.Standing.Snapshot;
        frame.SupportContacts = standing.SupportContactCount;
        frame.Standing = standing.IsStable;
        frame.WalkIntent = _buddy.CurrentDriveIntent.WalkDirection;
        frame.JumpIntent = _buddy.CurrentDriveIntent.JumpRequested;
        ActiveDriveComponent drive = _buddy.ActiveDrive;
        frame.AppliedDriveForce = drive.LastBalanceForce.Length() + drive.LastLocomotionForce.Length() +
                                  drive.LastGaitForce.Length() + drive.LastResistanceForce.Length();
        frame.TetherActive = _grab?.Telemetry.Active ?? false;
        frame.TetherStrain = _grab?.Telemetry.Extension ?? 0;
        frame.Consciousness = _buddy.CurrentConsciousness == Consciousness.Conscious
            ? TelemetryConsciousness.Conscious : TelemetryConsciousness.Unconscious;
        if (!_pending.TryAdd(frame)) _available.Enqueue(frame);
    }

    public void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        _pending.CompleteAdding();
        _writer?.Join();
    }

    public override void _ExitTree() => Complete();

    private void WriteLoop()
    {
        var reducer = new TelemetryEnvelopeReducer();
        using (var stream = new BufferedStream(File.Create(_jsonlPath), 64 * 1024))
        {
            foreach (TelemetryFrame frame in _pending.GetConsumingEnumerable())
            {
                TelemetrySerializer.WriteFrame(stream, frame);
                reducer.Add(frame);
                _available.Enqueue(frame);
            }
        }
        using Stream envelope = File.Create(_envelopePath);
        TelemetrySerializer.WriteEnvelope(envelope, reducer.Build());
    }
}

using System.IO;
using System.Text;
using DesktopBuddy.Domain.Telemetry;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Telemetry;

public sealed class TelemetryTests
{
    [Fact]
    public void ReducerComputesEnvelopeAcrossFrames()
    {
        var reducer = new TelemetryEnvelopeReducer();
        var frame = new TelemetryFrame(1, 1) { Standing = true, AppliedDriveForce = 4 };
        frame.Parts[0] = new PartTelemetry(0, 1, 2, 3, 2);
        frame.Links[0] = new LinkTelemetrySample("head", 10, 1.2f);
        reducer.Add(frame);
        frame.Standing = false; frame.AppliedDriveForce = 8;
        frame.Parts[0] = frame.Parts[0] with { Speed = 7 };
        reducer.Add(frame);
        TelemetryEnvelope result = reducer.Build();
        Assert.Equal(2, result.FrameCount);
        Assert.Equal(3, result.BodySpeed.Minimum);
        Assert.Equal(7, result.BodySpeed.Maximum);
        Assert.Equal(5, result.BodySpeed.Mean);
        Assert.Equal(1, result.StandingFrames);
    }

    [Fact]
    public void JsonLinesAndEnvelopeRoundTrip()
    {
        var frame = new TelemetryFrame(1, 0) { Tick = 42, Consciousness = TelemetryConsciousness.Conscious };
        frame.Parts[0] = new PartTelemetry(0, 1, 2, 3, 4);
        using var lines = new MemoryStream();
        TelemetrySerializer.WriteFrame(lines, frame);
        Assert.EndsWith("\n", Encoding.UTF8.GetString(lines.ToArray()));
        Assert.Contains("\"tick\":42", Encoding.UTF8.GetString(lines.ToArray()));
        Assert.Contains("\"consciousness\":\"conscious\"", Encoding.UTF8.GetString(lines.ToArray()));
        var expected = new TelemetryEnvelope(1, new(1, 2, 1.5), default, default, default, default, default, 1);
        using var summary = new MemoryStream();
        TelemetrySerializer.WriteEnvelope(summary, expected);
        summary.Position = 0;
        Assert.Equal(expected, TelemetrySerializer.ReadEnvelope(summary));
    }
}

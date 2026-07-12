using System;

namespace DesktopBuddy.Domain.Telemetry;

public readonly record struct MetricEnvelope(float Minimum, float Maximum, double Mean);

public readonly record struct TelemetryEnvelope(
    long FrameCount,
    MetricEnvelope BodySpeed,
    MetricEnvelope AngularSpeed,
    MetricEnvelope LinkSeparation,
    MetricEnvelope LinkStrain,
    MetricEnvelope DriveForce,
    MetricEnvelope TetherStrain,
    long StandingFrames);

public sealed class TelemetryEnvelopeReducer
{
    private readonly MetricAccumulator _bodySpeed = new();
    private readonly MetricAccumulator _angularSpeed = new();
    private readonly MetricAccumulator _linkSeparation = new();
    private readonly MetricAccumulator _linkStrain = new();
    private readonly MetricAccumulator _driveForce = new();
    private readonly MetricAccumulator _tetherStrain = new();
    private long _frames;
    private long _standingFrames;

    public void Add(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames++;
        if (frame.Standing) _standingFrames++;
        for (int i = 0; i < frame.Parts.Length; i++)
        {
            _bodySpeed.Add(frame.Parts[i].Speed);
            _angularSpeed.Add(frame.Parts[i].AngularSpeed);
        }
        for (int i = 0; i < frame.Links.Length; i++)
        {
            _linkSeparation.Add(frame.Links[i].Separation);
            _linkStrain.Add(frame.Links[i].Strain);
        }
        _driveForce.Add(frame.AppliedDriveForce);
        if (frame.TetherActive) _tetherStrain.Add(frame.TetherStrain);
    }

    public TelemetryEnvelope Build() => new(
        _frames, _bodySpeed.Build(), _angularSpeed.Build(), _linkSeparation.Build(),
        _linkStrain.Build(), _driveForce.Build(), _tetherStrain.Build(), _standingFrames);

    private sealed class MetricAccumulator
    {
        private float _min = float.PositiveInfinity;
        private float _max = float.NegativeInfinity;
        private double _sum;
        private long _count;
        public void Add(float value)
        {
            if (!float.IsFinite(value)) return;
            _min = MathF.Min(_min, value); _max = MathF.Max(_max, value); _sum += value; _count++;
        }
        public MetricEnvelope Build() => _count == 0
            ? new MetricEnvelope(0, 0, 0)
            : new MetricEnvelope(_min, _max, _sum / _count);
    }
}

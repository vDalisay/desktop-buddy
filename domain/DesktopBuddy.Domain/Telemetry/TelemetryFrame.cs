using System;

namespace DesktopBuddy.Domain.Telemetry;

public enum TelemetryConsciousness { Conscious, Unconscious }

public readonly record struct PartTelemetry(
    int PartId, float PositionX, float PositionY, float Speed, float AngularSpeed);

public readonly record struct LinkTelemetrySample(
    string LinkId, float Separation, float Strain);

/// <summary>Allocation-free payload reused by the lab recorder for one fixed tick.</summary>
public sealed class TelemetryFrame
{
    public long Tick { get; set; }
    public PartTelemetry[] Parts { get; }
    public LinkTelemetrySample[] Links { get; }
    public int SupportContacts { get; set; }
    public bool Standing { get; set; }
    public float WalkIntent { get; set; }
    public bool JumpIntent { get; set; }
    public float AppliedDriveForce { get; set; }
    public bool TetherActive { get; set; }
    public float TetherStrain { get; set; }
    public TelemetryConsciousness Consciousness { get; set; }

    public TelemetryFrame(int partCount, int linkCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(partCount);
        ArgumentOutOfRangeException.ThrowIfNegative(linkCount);
        Parts = new PartTelemetry[partCount];
        Links = new LinkTelemetrySample[linkCount];
    }
}

using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Sandbox;

/// <summary>
/// What a part does on the signal network (NF-4). <see cref="None"/> is an ordinary part. The Next
/// Fest vocabulary is deliberately small — the network itself is the full deterministic system, only
/// the devices plugged into it are restricted.
/// </summary>
public enum SandboxDeviceKind
{
    None = 0,
    Button,
    Timer,
    Piston,
    WeaponTrigger,
    Lamp,
}

public enum SandboxPortDirection
{
    Input = 0,
    Output = 1,
}

/// <summary>
/// One authored port on a device. Every Next Fest port carries a pulse, so there is no value kind
/// yet; one arrives with the first device that needs a held or scalar signal.
/// </summary>
public readonly record struct SandboxPort(string Name, SandboxPortDirection Direction);

public static class SandboxDevices
{
    public const string In = "in";
    public const string Out = "out";

    private static readonly SandboxPort[] OutputOnly = [new(Out, SandboxPortDirection.Output)];
    private static readonly SandboxPort[] InputOnly = [new(In, SandboxPortDirection.Input)];
    private static readonly SandboxPort[] InputAndOutput =
        [new(In, SandboxPortDirection.Input), new(Out, SandboxPortDirection.Output)];

    public static IReadOnlyList<SandboxPort> PortsFor(SandboxDeviceKind kind) => kind switch
    {
        SandboxDeviceKind.Button => OutputOnly,
        SandboxDeviceKind.Timer => InputAndOutput,
        SandboxDeviceKind.Piston or SandboxDeviceKind.WeaponTrigger or SandboxDeviceKind.Lamp => InputOnly,
        _ => [],
    };

    public static bool HasPort(SandboxDeviceKind kind, string port, SandboxPortDirection direction)
    {
        foreach (SandboxPort candidate in PortsFor(kind))
        {
            if (candidate.Direction == direction && string.Equals(candidate.Name, port, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

public readonly struct SandboxWireId : IEquatable<SandboxWireId>, IComparable<SandboxWireId>
{
    private SandboxWireId(Guid value) => Value = value;

    public Guid Value { get; }
    public bool IsValid => Value != Guid.Empty;
    public static SandboxWireId New() => new(Guid.NewGuid());

    public static SandboxWireId From(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A sandbox wire ID cannot be empty.", nameof(value));
        return new SandboxWireId(value);
    }

    public bool Equals(SandboxWireId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is SandboxWireId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public int CompareTo(SandboxWireId other) => Value.CompareTo(other.Value);
    public override string ToString() => IsValid ? Value.ToString("N") : string.Empty;
    public static bool operator ==(SandboxWireId left, SandboxWireId right) => left.Equals(right);
    public static bool operator !=(SandboxWireId left, SandboxWireId right) => !left.Equals(right);
}

/// <summary>A wire's colour: only a way to tell circuits apart; every colour carries the same pulse.</summary>
public enum SandboxWireColor
{
    Green = 0,
    Red,
    Blue,
    Yellow,
    Purple,
    White,
}

/// <summary>
/// One signal wire: from a device's output port to another device's input port. Wires are logic,
/// not physics — they pull on nothing, and a wire is drawn rather than simulated.
/// </summary>
public sealed record SandboxWire(
    SandboxWireId WireId,
    SandboxPartId From,
    string FromPort,
    SandboxPartId To,
    string ToPort,
    SandboxWireColor Color = SandboxWireColor.Green)
{
    public bool Touches(SandboxPartId partId) => From == partId || To == partId;

    /// <summary>The same output feeding the same input is one wire, not two.</summary>
    public bool Duplicates(SandboxWire other) =>
        From == other.From && To == other.To &&
        string.Equals(FromPort, other.FromPort, StringComparison.Ordinal) &&
        string.Equals(ToPort, other.ToPort, StringComparison.Ordinal);
}

public readonly record struct SandboxWireResult(
    SandboxLinkStatus Status,
    SandboxWire? Wire = null,
    string? Detail = null)
{
    public bool Succeeded => Status == SandboxLinkStatus.Succeeded;
}

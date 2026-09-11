using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.Domain.Sandbox;

public enum SandboxDeviceAction
{
    PistonExtend = 0,
    WeaponFire,
    LampOn,
    LampOff,
}

/// <summary>Something the room's runtime must do because a pulse reached a device.</summary>
public readonly record struct SandboxDeviceCommand(SandboxPartId Part, SandboxDeviceAction Action);

/// <summary>
/// The room's signal network (NF-4), engine-free and ticked once per routed fixed tick.
///
/// <para>A Timer is a clock: while it runs it sends a pulse every interval (its own setting). A
/// Timer nothing is wired into runs from the start; one with a wire in starts stopped, and each pulse
/// arriving switches it on or off — so Button → Timer → Piston is a switch for a repeating piston.</para>
///
/// <para>Two phases per tick. First the pulses due now are gathered — Buttons pressed since the
/// last tick and running Timers whose interval is up. Then each is delivered along its wires, in
/// stable part order, and the devices react. Nothing a device does in phase two can emit a pulse in
/// the same tick: a Timer switched on only schedules, so a loop of devices runs across ticks instead
/// of recursing inside one, and a tick's work is capped at <see cref="MaximumDeliveriesPerTick"/>.</para>
///
/// <para>The document is the truth. <see cref="Rebuild"/> re-reads it after any edit; state for
/// devices that are gone, or wires that were cut, simply stops mattering.</para>
/// </summary>
public sealed class SandboxSignalNetwork
{
    public const int MaximumDeliveriesPerTick = 4096;

    private readonly int _ticksPerSecond;
    private readonly Dictionary<SandboxPartId, SandboxDeviceKind> _devices = [];
    private readonly Dictionary<SandboxPartId, List<SandboxWire>> _wiresFrom = [];
    private readonly Dictionary<SandboxPartId, int> _timerInterval = [];
    /// <summary>Running Timers and the tick each next fires on; a stopped Timer is absent.</summary>
    private readonly Dictionary<SandboxPartId, long> _timerNext = [];
    private readonly HashSet<SandboxPartId> _litLamps = [];
    private readonly HashSet<SandboxPartId> _pressed = [];
    private readonly List<SandboxPartId> _emitting = [];

    public SandboxSignalNetwork(int ticksPerSecond)
    {
        if (ticksPerSecond < 1)
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond), "The network needs at least one tick a second.");
        _ticksPerSecond = ticksPerSecond;
    }

    public long TickCount { get; private set; }

    /// <summary>Deliveries turned away by the per-tick cap.</summary>
    public int DroppedPulses { get; private set; }

    public bool IsLampLit(SandboxPartId lamp) => _litLamps.Contains(lamp);

    public bool IsTimerRunning(SandboxPartId timer) => _timerNext.ContainsKey(timer);

    /// <summary>A Timer's interval in ticks, for verification.</summary>
    public int IntervalTicksOf(SandboxPartId timer) => _timerInterval.TryGetValue(timer, out int ticks) ? ticks : 0;

    public void Rebuild(SandboxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _devices.Clear();
        _wiresFrom.Clear();
        _timerInterval.Clear();
        foreach (PlacedSandboxPart part in document.Parts)
        {
            SandboxDeviceKind kind = document.DeviceOf(part.PartId);
            if (kind == SandboxDeviceKind.None)
                continue;
            _devices[part.PartId] = kind;
            if (kind == SandboxDeviceKind.Timer)
            {
                _timerInterval[part.PartId] = Math.Max(1,
                    (int)Math.Round(part.Overrides.TimerSecondsValue * _ticksPerSecond));
            }
        }
        foreach (SandboxWire wire in document.Wires)
        {
            if (!_wiresFrom.TryGetValue(wire.From, out List<SandboxWire>? outgoing))
                _wiresFrom[wire.From] = outgoing = [];
            outgoing.Add(wire);
        }
        // Stable delivery order within one source: by target, then by port.
        foreach (List<SandboxWire> outgoing in _wiresFrom.Values)
        {
            outgoing.Sort((left, right) =>
            {
                int byPart = left.To.CompareTo(right.To);
                return byPart != 0 ? byPart : string.CompareOrdinal(left.ToPort, right.ToPort);
            });
        }

        foreach (SandboxPartId gone in _timerNext.Keys.Where(id => KindOf(id) != SandboxDeviceKind.Timer).ToList())
            _timerNext.Remove(gone);
        var fed = new HashSet<SandboxPartId>(document.Wires.Select(wire => wire.To));
        foreach ((SandboxPartId timer, int interval) in _timerInterval)
        {
            if (_timerNext.TryGetValue(timer, out long next))
                _timerNext[timer] = Math.Min(next, TickCount + interval);   // a shortened interval applies now
            else if (!fed.Contains(timer))
                _timerNext[timer] = TickCount + interval;                   // a free Timer is a clock
        }
        _litLamps.RemoveWhere(id => KindOf(id) != SandboxDeviceKind.Lamp);
        _pressed.RemoveWhere(id => KindOf(id) != SandboxDeviceKind.Button);
    }

    /// <summary>A player pressed a Button; its pulse goes out on the next tick. False for anything else.</summary>
    public bool Press(SandboxPartId button)
    {
        if (KindOf(button) != SandboxDeviceKind.Button)
            return false;
        _pressed.Add(button);
        return true;
    }

    public void Tick(List<SandboxDeviceCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        TickCount++;

        // Phase one: what fires now.
        _emitting.Clear();
        _emitting.AddRange(_pressed);
        _pressed.Clear();
        foreach ((SandboxPartId timer, long next) in _timerNext)
        {
            if (next <= TickCount)
                _emitting.Add(timer);
        }
        foreach (SandboxPartId source in _emitting)
        {
            if (_timerNext.ContainsKey(source))
                _timerNext[source] = TickCount + _timerInterval[source];
        }
        _emitting.Sort();

        // Phase two: deliver. Reactions only schedule or command; none emits this tick.
        int delivered = 0;
        foreach (SandboxPartId source in _emitting)
        {
            if (!_wiresFrom.TryGetValue(source, out List<SandboxWire>? outgoing))
                continue;
            foreach (SandboxWire wire in outgoing)
            {
                if (delivered >= MaximumDeliveriesPerTick)
                {
                    DroppedPulses++;
                    continue;
                }
                delivered++;
                Receive(wire.To, commands);
            }
        }
    }

    private void Receive(SandboxPartId device, List<SandboxDeviceCommand> commands)
    {
        switch (KindOf(device))
        {
            case SandboxDeviceKind.Timer:
                // A pulse in switches the clock: off if running, else on with its first beat one
                // interval from now.
                if (!_timerNext.Remove(device))
                    _timerNext[device] = TickCount + _timerInterval[device];
                break;
            case SandboxDeviceKind.Piston:
                commands.Add(new SandboxDeviceCommand(device, SandboxDeviceAction.PistonExtend));
                break;
            case SandboxDeviceKind.WeaponTrigger:
                commands.Add(new SandboxDeviceCommand(device, SandboxDeviceAction.WeaponFire));
                break;
            case SandboxDeviceKind.Lamp:
                bool lit = !_litLamps.Remove(device);
                if (lit)
                    _litLamps.Add(device);
                commands.Add(new SandboxDeviceCommand(device, lit ? SandboxDeviceAction.LampOn : SandboxDeviceAction.LampOff));
                break;
        }
    }

    private SandboxDeviceKind KindOf(SandboxPartId part) =>
        _devices.TryGetValue(part, out SandboxDeviceKind kind) ? kind : SandboxDeviceKind.None;
}

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
/// <para>Two phases per tick. First the pulses due now are gathered — Buttons pressed since the
/// last tick and Timers whose wait is up. Then each is delivered along its wires, in stable part
/// order, and the devices react. Nothing a device does in phase two can emit a pulse in the same
/// tick: a Timer only ever schedules, so a loop of devices runs across ticks instead of recursing
/// inside one, and a tick's work is capped at <see cref="MaximumDeliveriesPerTick"/>.</para>
///
/// <para>The document is the truth. <see cref="Rebuild"/> re-reads it after any edit; pending
/// state for devices that are gone, or wires that were cut, simply stops mattering.</para>
/// </summary>
public sealed class SandboxSignalNetwork
{
    public const int MaximumDeliveriesPerTick = 256;

    /// <summary>A Timer holds at most this many pulses in flight; more arriving are dropped.</summary>
    public const int MaximumPendingPerTimer = 16;

    private readonly int _timerDelayTicks;
    private readonly Dictionary<SandboxPartId, SandboxDeviceKind> _devices = [];
    private readonly Dictionary<SandboxPartId, List<SandboxWire>> _wiresFrom = [];
    private readonly Dictionary<SandboxPartId, Queue<long>> _timerDue = [];
    private readonly HashSet<SandboxPartId> _litLamps = [];
    private readonly HashSet<SandboxPartId> _pressed = [];
    private readonly List<SandboxPartId> _emitting = [];

    public SandboxSignalNetwork(int timerDelayTicks)
    {
        if (timerDelayTicks < 1)
            throw new ArgumentOutOfRangeException(nameof(timerDelayTicks), "A Timer must wait at least one tick.");
        _timerDelayTicks = timerDelayTicks;
    }

    public long TickCount { get; private set; }

    /// <summary>Deliveries and Timer pulses turned away by the per-tick and per-Timer caps.</summary>
    public int DroppedPulses { get; private set; }

    public bool IsLampLit(SandboxPartId lamp) => _litLamps.Contains(lamp);

    /// <summary>Timer pulses still waiting, for verification and the Timer's own display.</summary>
    public int PendingAt(SandboxPartId timer) => _timerDue.TryGetValue(timer, out Queue<long>? due) ? due.Count : 0;

    public void Rebuild(SandboxDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _devices.Clear();
        _wiresFrom.Clear();
        foreach (PlacedSandboxPart part in document.Parts)
        {
            SandboxDeviceKind kind = document.DeviceOf(part.PartId);
            if (kind != SandboxDeviceKind.None)
                _devices[part.PartId] = kind;
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

        foreach (SandboxPartId gone in _timerDue.Keys.Where(id => KindOf(id) != SandboxDeviceKind.Timer).ToList())
            _timerDue.Remove(gone);
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
        foreach ((SandboxPartId timer, Queue<long> due) in _timerDue)
        {
            while (due.Count > 0 && due.Peek() <= TickCount)
            {
                due.Dequeue();
                _emitting.Add(timer);
            }
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
                if (!_timerDue.TryGetValue(device, out Queue<long>? due))
                    _timerDue[device] = due = new Queue<long>();
                if (due.Count >= MaximumPendingPerTimer)
                {
                    DroppedPulses++;
                    return;
                }
                due.Enqueue(TickCount + _timerDelayTicks);
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

using System.Collections.Generic;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// The live half of the room's devices (NF-4): the signal network ticked on the routed fixed tick,
/// and what its commands do to the parts. The network re-reads the active Scene's sandbox document
/// whenever that document changes, so every edit, Scene switch and restart takes the same path.
/// </summary>
public partial class SandboxRoot
{
    /// <summary>How long a Timer waits before passing its pulse on.</summary>
    private const double TimerDelaySeconds = 1.0;

    private readonly List<SandboxDeviceCommand> _deviceCommands = [];
    private SandboxSignalNetwork? _signals;
    private SandboxDocument? _signalDocument;
    private long _signalRevision = -1;

    /// <summary>The room's signal network, current with the active Scene's document.</summary>
    public SandboxSignalNetwork Signals
    {
        get
        {
            SyncSignals();
            return _signals!;
        }
    }

    /// <summary>The player pressed a Button; its pulse goes out on the next routed tick.</summary>
    public bool PressButton(SandboxPartId button) => Signals.Press(button);

    private void SyncSignals()
    {
        _signals ??= new SandboxSignalNetwork(
            Mathf.Max(1, Mathf.RoundToInt(TimerDelaySeconds * Engine.PhysicsTicksPerSecond)));
        if (SceneProgress is not { } scenes)
            return;
        SandboxDocument document = scenes.ActiveSandbox;
        if (ReferenceEquals(document, _signalDocument) && document.Revision == _signalRevision)
            return;
        _signals.Rebuild(document);
        _signalDocument = document;
        _signalRevision = document.Revision;
    }

    private void TickDevices()
    {
        if (SceneProgress is null)
            return;
        SandboxSignalNetwork signals = Signals;
        _deviceCommands.Clear();
        signals.Tick(_deviceCommands);
        // Piston and Weapon Trigger commands arrive with those devices' own packets.

        // Lamps show the network's state rather than replaying its commands, so a lamp rebuilt by
        // a cast change or a Scene switch comes back lit if it was lit.
        foreach ((SandboxPartId partId, SandboxPartBody body) in _builtParts)
        {
            if (body.Definition.Device == SandboxDeviceKind.Lamp && GodotObject.IsInstanceValid(body))
                body.Lit = signals.IsLampLit(partId);
        }

        // Wires follow the devices they join, and devices move.
        if (DocumentWires.Count > 0)
            _linkView?.QueueRedraw();
    }
}

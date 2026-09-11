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

    // ponytail: Piston feel is three constants, not a profile resource; promote when the owner tunes it.
    /// <summary>Speed a Piston gives what it shoves, whatever that weighs.</summary>
    private const float PistonPushSpeed = 650.0f;

    /// <summary>How long a Piston's head stays out; pulses arriving meanwhile are ignored.</summary>
    private const double PistonHoldSeconds = 0.25;

    /// <summary>Reach past the head's travel still counted as "in front of" the piston.</summary>
    private const float PistonShoveMargin = 6.0f;

    private const int MaximumPistonTargets = 32;

    private readonly List<SandboxDeviceCommand> _deviceCommands = [];

    /// <summary>Pistons that went out, for verification.</summary>
    public int PistonShoveCount { get; private set; }
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
        foreach (SandboxDeviceCommand command in _deviceCommands)
        {
            if (command.Action == SandboxDeviceAction.PistonExtend &&
                _builtParts.TryGetValue(command.Part, out SandboxPartBody? piston) &&
                GodotObject.IsInstanceValid(piston))
            {
                ExtendPiston(piston);
            }
        }
        // Weapon Trigger commands arrive with that device's own packet.

        // Lamps show the network's state rather than replaying its commands, so a lamp rebuilt by
        // a cast change or a Scene switch comes back lit if it was lit.
        foreach ((SandboxPartId partId, SandboxPartBody body) in _builtParts)
        {
            if (!GodotObject.IsInstanceValid(body))
                continue;
            if (body.Definition.Device == SandboxDeviceKind.Lamp)
                body.Lit = signals.IsLampLit(partId);
            else if (body.PistonTicksLeft > 0)
                body.PistonTicksLeft--;
        }

        // Wires follow the devices they join, and devices move.
        if (DocumentWires.Count > 0)
            _linkView?.QueueRedraw();
    }

    /// <summary>
    /// A Piston's head goes out and shoves whatever is in front of its face — parts, loose objects
    /// and Buddy parts alike — then stays out briefly before it can fire again. The push is a speed
    /// rather than an impulse, so a ball and a metal block both move. A piston that isn't frozen
    /// is pushed back by what it pushes against; nail it down to make it a wall.
    /// </summary>
    private void ExtendPiston(SandboxPartBody piston)
    {
        if (piston.PistonTicksLeft > 0)
            return;
        piston.PistonTicksLeft = Mathf.Max(1, Mathf.RoundToInt(PistonHoldSeconds * Engine.PhysicsTicksPerSecond));
        PistonShoveCount++;

        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
            return;

        float halfHeight = piston.Definition.Height * 0.5f;
        float depth = SandboxPartBody.PistonReach + PistonShoveMargin;
        using var shape = new RectangleShape2D { Size = new Vector2(piston.Definition.Width, depth) };
        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = shape,
            Transform = piston.GlobalTransform * new Transform2D(0.0f, new Vector2(0.0f, -halfHeight - depth * 0.5f)),
            CollisionMask = CollisionLayers.BuddyParts | CollisionLayers.LooseObjects,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };

        Vector2 push = Vector2.Up.Rotated(piston.GlobalRotation);
        float recoil = 0.0f;
        foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, MaximumPistonTargets))
        {
            if (!hit.TryGetValue("collider", out Variant value) ||
                value.AsGodotObject() is not RigidBody2D target ||
                target == piston ||
                target.Freeze)
            {
                continue;
            }
            float impulse = target.Mass * PistonPushSpeed;
            target.ApplyCentralImpulse(push * impulse);
            recoil += impulse;
        }
        if (!piston.Freeze && recoil > 0.0f)
            piston.ApplyCentralImpulse(-push * Mathf.Min(recoil, piston.Mass * PistonPushSpeed));
    }
}

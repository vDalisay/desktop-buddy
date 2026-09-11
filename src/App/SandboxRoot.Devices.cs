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
    /// <summary>Reach past the head's travel still counted as "in front of" the piston.</summary>
    private const float PistonShoveMargin = 6.0f;

    private const int MaximumPistonTargets = 32;
    private const int MaximumButtonContacts = 8;

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
    public bool PressButton(SandboxPartId button)
    {
        if (!Signals.Press(button))
            return false;
        if (_builtParts.TryGetValue(button, out SandboxPartBody? body) && GodotObject.IsInstanceValid(body))
            body.FlashButton();
        return true;
    }

    /// <summary>Buttons pressed by something landing on them rather than by a click, for verification.</summary>
    public int ButtonContactPresses { get; private set; }

    /// <summary>What last pressed a Button by landing on it, for verification.</summary>
    public string LastButtonPresser { get; private set; } = string.Empty;

    /// <summary>
    /// A Button is pressed by anything that comes down on its cap — a falling beam, a Buddy's foot,
    /// a Piston's head, a thrown tool (owner note 2026-09-11). It sends one pulse as it goes down and
    /// none while held; it must come back up before it can send another.
    /// </summary>
    private void SenseButtons(SandboxSignalNetwork signals)
    {
        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
            return;
        foreach ((SandboxPartId partId, SandboxPartBody body) in _builtParts)
        {
            if (body.Definition.Device != SandboxDeviceKind.Button || !GodotObject.IsInstanceValid(body))
                continue;

            Transform2D sensor = body.ButtonSensor(out Vector2 size);
            using var shape = new RectangleShape2D { Size = size };
            var query = new PhysicsShapeQueryParameters2D
            {
                Shape = shape,
                Transform = sensor,
                CollisionMask = CollisionLayers.BuddyParts | CollisionLayers.LooseObjects,
                CollideWithBodies = true,
                CollideWithAreas = false,
            };
            PhysicsBody2D? presser = null;
            foreach (Godot.Collections.Dictionary hit in space.IntersectShape(query, MaximumButtonContacts))
            {
                if (hit.TryGetValue("collider", out Variant value) && value.AsGodotObject() is PhysicsBody2D other && other != body)
                {
                    presser = other;
                    break;
                }
            }
            if (body.UpdateButton(presser is not null) && signals.Press(partId))
            {
                ButtonContactPresses++;
                LastButtonPresser = $"{presser!.Name} ({presser.GetType().Name}) at {presser.GlobalPosition}";
            }
        }
    }

    private void SyncSignals()
    {
        _signals ??= new SandboxSignalNetwork(Engine.PhysicsTicksPerSecond);
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
        SenseButtons(signals);
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
            else
                body.AdvancePistonStroke();
        }

        // Wires follow the devices they join, and devices move.
        if (DocumentWires.Count > 0)
            _linkView?.QueueRedraw();
    }

    /// <summary>
    /// A Piston's head goes out — its own collision shape, animated by the body — and shoves
    /// whatever is in front of its face: parts, loose objects and Buddy parts alike. The push is a
    /// speed (the Piston's Strength setting) rather than an impulse, so a ball and a metal block both
    /// move. Pulses arriving before the head is home are ignored. A piston that isn't frozen is
    /// pushed back by what it pushes against; nail it down to make it a wall.
    /// </summary>
    private void ExtendPiston(SandboxPartBody piston)
    {
        if (!piston.StartPistonStroke())
            return;
        PistonShoveCount++;

        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
            return;

        float halfHeight = piston.Definition.Height * 0.5f;
        float depth = SandboxPartLook.PistonReach + PistonShoveMargin;
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
            float impulse = target.Mass * piston.PistonPush;
            target.ApplyCentralImpulse(push * impulse);
            recoil += impulse;
        }
        if (!piston.Freeze && recoil > 0.0f)
            piston.ApplyCentralImpulse(-push * Mathf.Min(recoil, piston.Mass * piston.PistonPush));
    }
}

using System.Collections.Generic;
using System.Linq;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Domain.Tools;
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

    /// <summary>How far out of the mount its swung tool hangs, and how fast a pulse whips it round.</summary>
    private const float MountedToolReach = 34.0f;
    private const float MountedSwingSpeed = 26.0f;

    private readonly Dictionary<SandboxPartId, MountedTool> _mountedTools = [];

    private readonly List<SandboxDeviceCommand> _deviceCommands = [];

    /// <summary>Pistons that went out, for verification.</summary>
    public int PistonShoveCount { get; private set; }

    /// <summary>Shots a built weapon mount fired, for verification.</summary>
    public int WeaponMountShots { get; private set; }
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
            if (!_builtParts.TryGetValue(command.Part, out SandboxPartBody? device) || !GodotObject.IsInstanceValid(device))
                continue;
            switch (command.Action)
            {
                case SandboxDeviceAction.PistonExtend:
                    ExtendPiston(device);
                    break;
                case SandboxDeviceAction.WeaponFire:
                    FireWeaponMount(device);
                    break;
            }
        }

        // Lamps show the network's state rather than replaying its commands, so a lamp rebuilt by
        // a cast change or a Scene switch comes back lit if it was lit.
        foreach ((SandboxPartId partId, SandboxPartBody body) in _builtParts)
        {
            if (!GodotObject.IsInstanceValid(body))
                continue;
            if (body.Definition.Device == SandboxDeviceKind.Lamp)
                body.Lit = signals.IsLampLit(partId);
            else
                body.AdvanceDevice();
        }

        DropStrandedMountedTools();

        // Wires follow the devices they join, and devices move.
        if (DocumentWires.Count > 0)
            _linkView?.QueueRedraw();
    }

    /// <summary>
    /// A Tool Mount uses the tool it holds (NF-4D, owner 2026-09-12: one mount, any tool). A gun
    /// fires along the way the part is turned; a swung tool — glove, bat, sword — is a real tool
    /// body hinged to the mount, and the pulse whips it round, so what it hits and how much it hurts
    /// come from the same physics and the same pipeline as the tool in the player's hand. The
    /// mount's own recoil is the fastest it can be used, so a quick Timer cannot make a machine gun
    /// of a pistol or a blender of a bat.
    /// </summary>
    private void FireWeaponMount(SandboxPartBody mount)
    {
        if (SandboxMountableTools.ToolOf(mount.MountedTool) is not { } tool || !mount.StartWeaponShot())
            return;

        if (SandboxMountableTools.IsGun(tool))
        {
            if (!GodotObject.IsInstanceValid(CursorGuns))
                return;
            Vector2 forward = Vector2.Right.Rotated(mount.GlobalRotation);
            Vector2 barrel = mount.GlobalPosition + forward * (mount.Definition.Width * 0.5f);
            if (CursorGuns.FireMounted(tool, barrel, forward))
                WeaponMountShots++;
            return;
        }
        if (SwingArmOf(mount, tool) is { } arm)
        {
            // A whip round the pin. Spin alone would only turn the tool on the spot and fight the
            // pin; the matching tangential speed is what actually carries it round the mount.
            arm.Sleeping = false;
            Vector2 fromPin = arm.GlobalPosition - mount.GlobalPosition;
            arm.AngularVelocity = MountedSwingSpeed;
            arm.LinearVelocity = new Vector2(-fromPin.Y, fromPin.X) * MountedSwingSpeed;
            WeaponMountShots++;
        }
    }

    /// <summary>
    /// The tool body a mount swings, hinged to it, made the first time that mount is used and kept
    /// while it stands. A mount whose tool changed drops the old body and holds the new one.
    /// </summary>
    private RigidBody2D? SwingArmOf(SandboxPartBody mount, ToolId tool)
    {
        if (_mountedTools.TryGetValue(mount.PartId, out MountedTool held))
        {
            if (held.Tool == tool && GodotObject.IsInstanceValid(held.Body))
                return held.Body;
            if (GodotObject.IsInstanceValid(held.Body))
                held.Body!.QueueFree();
            if (GodotObject.IsInstanceValid(held.Joint))
                held.Joint!.QueueFree();
            _mountedTools.Remove(mount.PartId);
        }
        if (!GodotObject.IsInstanceValid(CursorTools) || CursorTools.CreateHeldTool(tool, this) is not { } body)
            return null;

        // Hung from the mount's face, so it swings clear of the mount itself.
        body.GlobalPosition = mount.GlobalPosition + Vector2.Right.Rotated(mount.GlobalRotation) * MountedToolReach;
        var joint = new PinJoint2D { Name = $"MountPin_{mount.PartId}" };
        AddChild(joint);
        // Every one of these needs the joint in the tree first: a PinJoint2D binds its bodies when
        // its paths are set, and a path set before it is inside the tree resolves to nothing.
        joint.GlobalPosition = mount.GlobalPosition;
        joint.NodeA = joint.GetPathTo(mount);
        joint.NodeB = joint.GetPathTo(body);
        _mountedTools[mount.PartId] = new MountedTool(tool, body, joint);
        return body;
    }

    private readonly record struct MountedTool(ToolId Tool, RigidBody2D? Body, PinJoint2D? Joint);

    /// <summary>
    /// A mount that has been deleted, or carried off by a Scene switch, takes its tool with it: the
    /// hinged body would otherwise be left lying in the room with nothing holding it.
    /// </summary>
    private void DropStrandedMountedTools()
    {
        if (_mountedTools.Count == 0)
            return;
        foreach (SandboxPartId partId in _mountedTools.Keys.ToList())
        {
            if (_builtParts.TryGetValue(partId, out SandboxPartBody? mount) && GodotObject.IsInstanceValid(mount) &&
                SandboxMountableTools.ToolOf(mount.MountedTool) == _mountedTools[partId].Tool)
            {
                continue;
            }
            MountedTool held = _mountedTools[partId];
            if (GodotObject.IsInstanceValid(held.Joint))
                held.Joint!.QueueFree();
            if (GodotObject.IsInstanceValid(held.Body))
                held.Body!.QueueFree();
            _mountedTools.Remove(partId);
        }
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

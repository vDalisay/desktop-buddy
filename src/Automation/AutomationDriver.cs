using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using Godot;

namespace DesktopBuddy.Automation;

/// <summary>
/// Development-only automation surface (AGENT_VERIFICATION_AND_E2E.md Section 2).
/// Composed only in debug builds when a runner or <c>--automation</c> is present;
/// release exports contain no automation code paths.
///
/// Input synthesis routes through <see cref="Input.ParseInputEvent"/> so
/// synthesized pointer/key events enter the same Godot input queue, the same
/// InputCollector, and the same immutable ToolInputFrame path as real input —
/// journeys never poke gameplay components directly. State queries are
/// read-only: providers register a snapshot getter so journeys can assert on
/// telemetry without the game depending on this driver.
///
/// Milestone 0 provides the skeleton (input primitives + state registry); the
/// semantic anchor resolution and record-and-promote tracing land in Milestone 1
/// alongside the first journeys that need them.
/// </summary>
public partial class AutomationDriver : Node
{
    private readonly Dictionary<string, Func<Variant>> _stateProviders = new();
    private readonly List<InputTraceEvent> _trace = new();
    private RunnerArguments _args = new();

    public void Configure(RunnerArguments args) => _args = args;

    public override void _Ready()
    {
        Log.Info("Automation", "AutomationDriver active (development build).");
        if (_args.PromoteTrace is not null)
        {
            InputTrace? trace = JsonSerializer.Deserialize<InputTrace>(File.ReadAllText(_args.PromoteTrace));
            if (trace is null) throw new InvalidDataException("Input trace is empty or invalid.");
            string output = TracePromoter.Promote(trace, Path.GetFileNameWithoutExtension(_args.JourneyOut!) ?? "TODO_trace_journey");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_args.JourneyOut!))!);
            File.WriteAllText(_args.JourneyOut!, output);
            Log.Info("Automation", $"Promoted trace to {_args.JourneyOut}.");
            GetTree().Quit();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (_args.TraceOut is null) return;
        long tick = (long)Engine.GetPhysicsFrames();
        if (@event is InputEventMouseButton button)
        {
            string kind = button.Pressed ? "pointer_press" : "pointer_release";
            _trace.Add(new InputTraceEvent(tick, kind, ResolveAnchor(button.Position), button.Position.X, button.Position.Y, (int)button.ButtonIndex));
        }
        else if (@event is InputEventMouseMotion motion)
            _trace.Add(new InputTraceEvent(tick, "pointer_motion", ResolveAnchor(motion.Position), motion.Position.X, motion.Position.Y));
        else if (@event is InputEventKey { Pressed: true, Echo: false } key)
            _trace.Add(new InputTraceEvent(tick, "key_press", "keyboard", 0, 0, 0, (int)key.PhysicalKeycode));
    }

    public override void _ExitTree()
    {
        if (_args.TraceOut is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_args.TraceOut))!);
        var trace = new InputTrace("desktop-buddy-input-trace-v1", _args.Seed ?? 0, _trace);
        File.WriteAllText(_args.TraceOut, JsonSerializer.Serialize(trace, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string ResolveAnchor(Vector2 position)
    {
        PuppetPartBody? nearest = null;
        float distance = 32.0f;
        foreach (Node node in GetTree().GetNodesInGroup("buddy_parts"))
        {
            if (node is not PuppetPartBody part) continue;
            float candidate = part.GlobalPosition.DistanceTo(position);
            if (candidate <= distance) { distance = candidate; nearest = part; }
        }
        return nearest is null ? "sandbox" : $"buddy:{nearest.PartId}";
    }

    // --- Input synthesis (real input path) ---

    public void SynthesizeAction(string action, bool pressed, float strength = 1.0f)
    {
        var ev = new InputEventAction { Action = action, Pressed = pressed, Strength = strength };
        Input.ParseInputEvent(ev);
    }

    public void SynthesizeKey(Key physicalKeycode, bool pressed)
    {
        var ev = new InputEventKey { PhysicalKeycode = physicalKeycode, Pressed = pressed };
        Input.ParseInputEvent(ev);
    }

    public void SynthesizeMouseButton(MouseButton button, Vector2 position, bool pressed)
    {
        var ev = new InputEventMouseButton
        {
            ButtonIndex = button,
            Pressed = pressed,
            Position = position,
            GlobalPosition = position,
        };
        Input.ParseInputEvent(ev);
    }

    public void SynthesizeMouseMotion(Vector2 position, Vector2 relative)
    {
        var ev = new InputEventMouseMotion
        {
            Position = position,
            GlobalPosition = position,
            Relative = relative,
        };
        Input.ParseInputEvent(ev);
    }

    // --- Read-only state registry (journey assertions) ---

    /// <summary>Register a read-only state getter keyed by a stable anchor name.</summary>
    public void RegisterState(string key, Func<Variant> provider) => _stateProviders[key] = provider;

    /// <summary>Resolve a registered state value; false if the key is unknown.</summary>
    public bool TryQueryState(string key, out Variant value)
    {
        if (_stateProviders.TryGetValue(key, out Func<Variant>? provider))
        {
            value = provider();
            return true;
        }

        value = default;
        return false;
    }
}

using System;
using System.Collections.Generic;
using DesktopBuddy.Diagnostics;
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

    public override void _Ready()
    {
        Log.Info("Automation", "AutomationDriver active (development build).");
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

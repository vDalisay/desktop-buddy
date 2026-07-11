using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Buddy;
using Godot;

namespace DesktopBuddy.Buddy.Behavior;

/// <summary>Immutable fear-resistance request: move away from the grab, scaled by fear.</summary>
public readonly record struct GrabResistanceIntent(bool Active, float Direction, float Strength);

/// <summary>
/// Produces the buddy's opposing-motion intent when one of its parts is grabbed
/// while conscious and fearful (RAGDOLL_AND_GAMEPLAY_SPEC.md Section 6). It
/// selects intent only — the active drive applies the bounded force, and the
/// tether itself stays physically active regardless of intent. An unconscious
/// buddy produces no resistance.
///
/// Until the mood/memory system lands (Milestone 4), fear is injected directly
/// through <see cref="FearLevel"/> by the laboratory; the resistance mechanism
/// itself is production code.
/// </summary>
[GlobalClass]
public partial class GrabResistanceComponent : Node
{
    [Export] public PuppetRig Rig { get; set; } = null!;

    /// <summary>Laboratory stand-in for mood/memory-driven fear, in [0, 1].</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float FearLevel { get; set; }

    private bool _buddyPartGrabbed;
    private Vector2 _cursorAnchor;

    public GrabResistanceIntent Intent { get; private set; }
    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Rig) || !Rig.IsInitialized)
        {
            throw new InvalidOperationException("GrabResistanceComponent requires an initialized PuppetRig.");
        }

        IsInitialized = true;
    }

    /// <summary>Pushed by the sandbox/lab each tick before the buddy ticks.</summary>
    public void SetGrabContext(bool buddyPartGrabbed, Vector2 cursorAnchor)
    {
        _buddyPartGrabbed = buddyPartGrabbed;
        _cursorAnchor = cursorAnchor;
    }

    public void PhysicsTick(Consciousness consciousness)
    {
        float fear = Mathf.Clamp(FearLevel, 0.0f, 1.0f);
        bool active = consciousness == Consciousness.Conscious && _buddyPartGrabbed && fear > 0.0f;
        if (!active)
        {
            Intent = new GrabResistanceIntent(false, 0.0f, 0.0f);
            return;
        }

        // Move away from the cursor horizontally; deterministic tiebreak when aligned.
        float away = Rig.Torso.GlobalPosition.X >= _cursorAnchor.X ? 1.0f : -1.0f;
        Intent = new GrabResistanceIntent(true, away, fear);
    }
}

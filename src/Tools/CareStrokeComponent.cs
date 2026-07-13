using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Interaction;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Pet/Tickle stroke detection (RAGDOLL §8.2, §9.1): a held primary stroke counts
/// as valid care contact only while the cursor is over a real buddy body, and the
/// accumulated valid-contact seconds feed the Domain care cadence through the
/// pipeline. Holding input over empty space accumulates nothing, and care never
/// awards money. Contact validity is a distance test against the six part circles
/// (plus a small slop, matching the lab pointer's pick tolerance) so headless
/// scenarios and the pointer harness share one deterministic rule.
/// </summary>
[GlobalClass]
public partial class CareStrokeComponent : Node
{
    private const float ContactSlopPixels = 8.0f;

    [Export] public InteractionDamageComponent Pipeline { get; set; } = null!;

    private bool _held;
    private Vector2 _cursor;

    public bool IsInitialized { get; private set; }
    public bool LastContactValid { get; private set; }
    public long ValidContactTicks { get; private set; }

    public void Initialize()
    {
        if (!GodotObject.IsInstanceValid(Pipeline))
        {
            throw new InvalidOperationException("CareStrokeComponent requires the interaction pipeline.");
        }

        IsInitialized = true;
    }

    /// <summary>Latest stroke state in sandbox coordinates; holds until replaced.</summary>
    public void SetStroke(bool held, Vector2 worldPoint)
    {
        _held = held;
        _cursor = worldPoint;
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick(double delta)
    {
        RequireInitialized();
        LastContactValid = false;

        CareKind? kind = ToolCatalog.CareKindOf(Pipeline.SelectedTool);
        if (kind is null || !_held || !IsOverBuddy(_cursor))
        {
            return;
        }

        LastContactValid = true;
        ValidContactTicks++;
        Pipeline.AccumulateCare(kind.Value, delta);
    }

    private bool IsOverBuddy(Vector2 world)
    {
        IReadOnlyList<PuppetPartBody> parts = Pipeline.Buddy.Rig.Parts;
        for (int index = 0; index < parts.Count; index++)
        {
            PuppetPartBody part = parts[index];
            float reach = part.Radius + ContactSlopPixels;
            if (part.GlobalPosition.DistanceSquaredTo(world) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    private void RequireInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("CareStrokeComponent used before initialization.");
        }
    }
}

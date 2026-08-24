using System;
using System.Collections.Generic;
using Godot;

namespace DesktopBuddy.Grab;

/// <summary>One rope: the body it holds, where it holds it, and what it is tied to.</summary>
public readonly record struct SuspensionRope(
    RigidBody2D Body,
    Vector2 LocalPoint,
    Vector2 Anchor);

/// <summary>
/// The Rope Suspender's ropes. Grabbing is still the shared
/// <see cref="GrabTetherController"/>'s job; this component only holds what the player has
/// already picked up: a rope ties the grabbed point to the spot the player clicked, the hand
/// is free again, and the object hangs there until the rope is cut.
///
/// <para>Same bounded damped-elastic pull the player tether uses, applied on the routed fixed
/// tick. It never acquires, releases, damages, or scores — a rope is a force and a drawn line,
/// nothing more.</para>
/// </summary>
[GlobalClass]
public partial class RopeSuspensionComponent : Node2D
{
    /// <summary>Orange, so a rope is never mistaken for the pale player tether.</summary>
    private static readonly Color RopeColor = Color.Color8(232, 140, 40);

    private readonly List<SuspensionRope> _ropes = [];

    /// <summary>Pull toward the anchor, per pixel of offset, per unit mass.</summary>
    [Export(PropertyHint.Range, "1,4000,1,or_greater")] public float Stiffness { get; set; } = 900.0f;

    /// <summary>Velocity damping, so a hung object settles instead of bouncing on its rope.</summary>
    [Export(PropertyHint.Range, "0,400,0.5,or_greater")] public float Damping { get; set; } = 42.0f;

    /// <summary>Force ceiling, so no rope can launch what it holds.</summary>
    [Export(PropertyHint.Range, "100,200000,10,or_greater")] public float MaximumForce { get; set; } = 90000.0f;

    /// <summary>How near a rope the pointer must be to cut it.</summary>
    [Export(PropertyHint.Range, "2,64,0.5,or_greater")] public float CutRadiusPx { get; set; } = 12.0f;

    public bool IsInitialized { get; private set; }

    public IReadOnlyList<SuspensionRope> Ropes => _ropes;
    public int RopeCount => _ropes.Count;

    /// <summary>Ropes tied since this component was initialized — the scenario oracle.</summary>
    public int AttachCount { get; private set; }

    /// <summary>Ropes cut by the player since this component was initialized.</summary>
    public int CutCount { get; private set; }

    public void Initialize()
    {
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        IsInitialized = true;
    }

    /// <summary>
    /// Ties <paramref name="body"/> to <paramref name="anchor"/> by the world point it is
    /// currently held at. A body may hold only one rope: a second click on an already-hung
    /// object moves its rope rather than stacking a second pull on it.
    /// </summary>
    public bool Attach(RigidBody2D body, Vector2 grabWorldPoint, Vector2 anchor)
    {
        if (!IsInitialized || !GodotObject.IsInstanceValid(body))
            return false;

        Vector2 local = body.ToLocal(grabWorldPoint);
        _ropes.RemoveAll(rope => rope.Body == body);
        _ropes.Add(new SuspensionRope(body, local, anchor));
        AttachCount++;
        QueueRedraw();
        return true;
    }

    /// <summary>Cuts the rope the pointer is over, if any. Returns whether one was cut.</summary>
    public bool TryCutAt(Vector2 world)
    {
        for (int index = 0; index < _ropes.Count; index++)
        {
            SuspensionRope rope = _ropes[index];
            if (!GodotObject.IsInstanceValid(rope.Body))
                continue;
            if (DistanceToSegment(world, rope.Anchor, rope.Body.ToGlobal(rope.LocalPoint)) > CutRadiusPx)
                continue;
            _ropes.RemoveAt(index);
            CutCount++;
            QueueRedraw();
            return true;
        }

        return false;
    }

    /// <summary>Whether a rope passes under this point — what the cut cursor is shown for.</summary>
    public bool IsOverRope(Vector2 world)
    {
        foreach (SuspensionRope rope in _ropes)
        {
            if (GodotObject.IsInstanceValid(rope.Body) &&
                DistanceToSegment(world, rope.Anchor, rope.Body.ToGlobal(rope.LocalPoint)) <= CutRadiusPx)
            {
                return true;
            }
        }

        return false;
    }

    public void CutAll()
    {
        if (_ropes.Count == 0)
            return;
        _ropes.Clear();
        QueueRedraw();
    }

    /// <summary>Called only from the owning root's routed fixed tick.</summary>
    public void PhysicsTick(double delta)
    {
        if (!IsInitialized || delta <= 0.0 || _ropes.Count == 0)
            return;

        // Runtime bodies may disappear through reset/eviction. Prune in-place instead of invoking
        // a predicate over the list every 120 Hz tick; more importantly, the empty-rope path above
        // now performs no redraw work at all.
        for (int index = _ropes.Count - 1; index >= 0; index--)
        {
            if (!GodotObject.IsInstanceValid(_ropes[index].Body))
                _ropes.RemoveAt(index);
        }

        for (int index = 0; index < _ropes.Count; index++)
        {
            SuspensionRope rope = _ropes[index];
            Vector2 held = rope.Body.ToGlobal(rope.LocalPoint);
            Vector2 offset = rope.Anchor - held;
            Vector2 force = (offset * Stiffness * rope.Body.Mass) -
                (rope.Body.LinearVelocity * Damping * rope.Body.Mass);
            float length = force.Length();
            if (length > MaximumForce)
                force *= MaximumForce / length;
            if (!float.IsFinite(force.X) || !float.IsFinite(force.Y))
                continue;
            rope.Body.ApplyForce(force, held - rope.Body.GlobalPosition);
        }

        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (SuspensionRope rope in _ropes)
        {
            if (!GodotObject.IsInstanceValid(rope.Body))
                continue;
            Vector2 held = rope.Body.ToGlobal(rope.LocalPoint);
            DrawLine(rope.Anchor, held, RopeColor, 2.5f, true);
            DrawCircle(rope.Anchor, 4.0f, RopeColor);
        }
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 span = end - start;
        float lengthSquared = span.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return point.DistanceTo(start);
        float t = Math.Clamp((point - start).Dot(span) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + (span * t));
    }
}

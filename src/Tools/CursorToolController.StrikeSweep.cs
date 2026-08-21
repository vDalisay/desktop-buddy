using System;
using DesktopBuddy.App;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>One loose object a cursor tool connected with on a routed tick.</summary>
public readonly record struct LooseObjectStrike(
    string ContentId,
    LooseObjectBody Body,

    /// <summary>Speed of the tool's own surface where it met the object, in px/s.</summary>
    float Speed,

    /// <summary>
    /// True when the solver never saw this contact and the sweep had to deliver it — the
    /// tool passed clean through between two steps.
    /// </summary>
    bool Tunnelled);

/// <summary>
/// Swept strike detection for cursor tools (owner report 2026-08-21: "the bat tends to swing
/// through the grenade").
///
/// <para>Godot's continuous collision detection extrapolates <b>linear</b> motion only. A bat
/// swings by rotating around a grip near one end, so the barrel tip covers far more ground in
/// one step than the body's centre does, and CCD never sees it. Against a buddy part — radius
/// 24 — that does not matter; against a grenade or a repair kit it means the barrel is on one
/// side at the start of the step and the other side at the end, with no contact in between.</para>
///
/// <para>So every routed tick the tool's own shape is walked from where it was to where it is,
/// rotation included, and anything it passed over is reported. Two separate outcomes come out
/// of that walk, deliberately kept apart:</para>
/// <list type="bullet">
///   <item><b>The strike</b> — a semantic event, raised whenever the tool's surface was moving
///   faster than <see cref="MinimumStrikeSpeedPx"/> where it met the object. It fires for
///   contacts the solver handled perfectly well too, because "the bat hit the grenade" is a
///   fact about the game, not about whether the solver noticed.</item>
///   <item><b>The repair impulse</b> — applied only when the object was passed over but is not
///   touching at the end of the step, which is exactly the tunnelling case. A contact the
///   solver did see is left entirely alone, so nothing is ever counted twice.</item>
/// </list>
/// </summary>
public partial class CursorToolController
{
    /// <summary>
    /// How fast the tool's surface must be moving where it meets an object to count as a hit
    /// rather than a nudge. Well above the 60 px/s the bat needs to steer, so carrying a tool
    /// across the room past a ball is not a strike.
    /// </summary>
    private const float MinimumStrikeSpeedPx = 300.0f;

    /// <summary>Ticks one object is immune to a second strike, so one swing lands once.</summary>
    private const int StrikeCooldownTicks = 20;

    private const int MaxTrackedStrikes = 8;
    private const int MaxSweepSubsteps = 10;
    private const int MaxSweepResults = 8;

    private readonly ulong[] _recentStrikeIds = new ulong[MaxTrackedStrikes];
    private readonly int[] _recentStrikeTicks = new int[MaxTrackedStrikes];
    private Transform2D _previousBodyTransform;
    private bool _hasPreviousBodyTransform;
    private Shape2D? _sweepShape;
    private int _sweepTick;

    /// <summary>Strikes the sweep had to deliver itself, for readouts and scenarios.</summary>
    public int TunnelledStrikeCount { get; private set; }

    /// <summary>Every loose object this tool connected with, tunnelled or not.</summary>
    public int LooseObjectStrikeCount { get; private set; }

    public event Action<LooseObjectStrike>? LooseObjectStruck;

    private void SweepStrikes(CursorToolBody body, CursorToolProfile profile)
    {
        _sweepTick++;
        Transform2D current = body.GlobalTransform;
        if (!_hasPreviousBodyTransform || !body.IsImpactArmed)
        {
            _previousBodyTransform = current;
            _hasPreviousBodyTransform = true;
            return;
        }

        Transform2D previous = _previousBodyTransform;
        _previousBodyTransform = current;

        PhysicsDirectSpaceState2D? space = GetWorld2D()?.DirectSpaceState;
        if (space is null)
            return;

        _sweepShape ??= profile.IsElongated
            ? new CapsuleShape2D { Radius = profile.Radius, Height = profile.Length }
            : new CircleShape2D { Radius = profile.Radius };

        // The tip is what tunnels, so the tip's travel — not the centre's — decides how finely
        // the step has to be walked. One substep per tool radius keeps the samples overlapping.
        float reach = profile.IsElongated ? profile.Length * 0.5f : profile.Radius;
        float tipTravel = previous.Origin.DistanceTo(current.Origin) +
            (Mathf.Abs(Mathf.AngleDifference(previous.Rotation, current.Rotation)) * reach);
        int substeps = Mathf.Clamp(
            Mathf.CeilToInt(tipTravel / Mathf.Max(1.0f, profile.Radius)), 1, MaxSweepSubsteps);

        var query = new PhysicsShapeQueryParameters2D
        {
            Shape = _sweepShape,
            CollisionMask = CollisionLayers.LooseObjects,
            CollideWithBodies = true,
            CollideWithAreas = false,
        };

        for (int step = 1; step <= substeps; step++)
        {
            float t = step / (float)substeps;
            query.Transform = new Transform2D(
                previous.Rotation + (Mathf.AngleDifference(previous.Rotation, current.Rotation) * t),
                previous.Origin.Lerp(current.Origin, t));
            Godot.Collections.Array<Godot.Collections.Dictionary> hits =
                space.IntersectShape(query, MaxSweepResults);
            foreach (Godot.Collections.Dictionary hit in hits)
            {
                if (!hit.TryGetValue("collider", out Variant value) ||
                    value.AsGodotObject() is not LooseObjectBody target ||
                    !GodotObject.IsInstanceValid(target) || target.Freeze)
                {
                    continue;
                }

                ResolveSweptContact(body, target, current, step == substeps);
            }
        }
    }

    private void ResolveSweptContact(
        CursorToolBody body,
        LooseObjectBody target,
        Transform2D current,
        bool touchingAtStepEnd)
    {
        ulong id = target.GetInstanceId();
        if (!TryClaimStrike(id))
            return;

        // The velocity of the tool's own surface where the object is, not of its centre:
        // for a swung bat those differ by most of the impact.
        Vector2 lever = target.GlobalPosition - current.Origin;
        Vector2 surfaceVelocity = body.LinearVelocity +
            (new Vector2(-lever.Y, lever.X) * body.AngularVelocity);
        float speed = surfaceVelocity.Length();
        if (speed < MinimumStrikeSpeedPx)
        {
            ReleaseStrike(id);
            return;
        }

        // The solver has this one in hand; reporting it is all we owe.
        if (!touchingAtStepEnd)
        {
            target.ApplyCentralImpulse(surfaceVelocity.Normalized() * (target.Mass * speed));
            TunnelledStrikeCount++;
        }

        LooseObjectStrikeCount++;
        LooseObjectStruck?.Invoke(
            new LooseObjectStrike(body.ContentId, target, speed, !touchingAtStepEnd));
    }

    /// <summary>
    /// Reserves the strike slot for one object, or refuses while its cooldown runs. Fixed
    /// slots rather than a dictionary: this runs inside the physics tick, where the registry
    /// allocation probe is watching.
    /// </summary>
    private bool TryClaimStrike(ulong id)
    {
        int free = -1;
        for (int index = 0; index < MaxTrackedStrikes; index++)
        {
            if (_recentStrikeIds[index] == id)
            {
                if (_sweepTick - _recentStrikeTicks[index] < StrikeCooldownTicks)
                    return false;
                _recentStrikeTicks[index] = _sweepTick;
                return true;
            }

            if (free < 0 &&
                (_recentStrikeIds[index] == 0 ||
                 _sweepTick - _recentStrikeTicks[index] >= StrikeCooldownTicks))
            {
                free = index;
            }
        }

        if (free < 0)
            return false;
        _recentStrikeIds[free] = id;
        _recentStrikeTicks[free] = _sweepTick;
        return true;
    }

    private void ReleaseStrike(ulong id)
    {
        for (int index = 0; index < MaxTrackedStrikes; index++)
        {
            if (_recentStrikeIds[index] == id)
            {
                _recentStrikeIds[index] = 0;
                return;
            }
        }
    }

    private void ResetStrikeSweep()
    {
        _hasPreviousBodyTransform = false;
        _sweepShape?.Dispose();
        _sweepShape = null;
        Array.Clear(_recentStrikeIds);
        Array.Clear(_recentStrikeTicks);
    }
}

using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.Sandbox;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// The live half of the room's links (NF-3): Godot joints for hinges and welds, a bounded force for
/// ropes, and the drawing of all three. Like the parts themselves, links hold no durable state here:
/// the Scene's sandbox document is the truth and this rebuilds from it whenever it changes.
/// </summary>
public partial class SandboxRoot
{
    /// <summary>Ropes reuse the Rope Suspender's bounded damped pull, so neither can outmuscle the other.</summary>
    private const float RopeStiffness = 900.0f;
    private const float RopeDamping = 42.0f;
    private const float RopeMaximumForce = 90000.0f;

    /// <summary>A weld is two pins this far apart, which is what stops the parts turning.</summary>
    private const float WeldPinSpacing = 20.0f;

    private readonly List<BuiltLink> _builtLinks = [];
    private SandboxLinkView? _linkView;

    /// <summary>One document link made live: its joints (hinge/weld) or its rope ends.</summary>
    private sealed class BuiltLink(SandboxLink link, SandboxPartBody a, SandboxPartBody? b)
    {
        public SandboxLink Link { get; } = link;
        public SandboxPartBody A { get; } = a;
        public SandboxPartBody? B { get; } = b;
        public List<Node> Nodes { get; } = [];
    }

    /// <summary>The links standing in the room, for verification.</summary>
    public int BuiltLinkCount => _builtLinks.Count;

    /// <summary>
    /// Drops every live link and makes the document's links again against the parts as they stand
    /// now. Build calls it after any edit that touches a link, while the room is paused, so joints
    /// always start from the relative placement the player is looking at.
    /// </summary>
    public void RebuildBuiltLinks()
    {
        ClearBuiltLinks();
        if (SceneProgress is not { } scenes)
            return;
        EnsureLinkView();
        foreach (SandboxLink link in scenes.ActiveSandbox.Links)
            SpawnBuiltLink(link);
        _linkView?.QueueRedraw();
    }

    /// <summary>Where a link end is in the room right now, or null if its part is not standing.</summary>
    public Vector2? LinkEndWorld(SandboxLinkEnd end)
    {
        if (end.IsWorld)
            return CanonicalToWorld(end.X, end.Y);
        return _builtParts.TryGetValue(end.PartId, out SandboxPartBody? body) && GodotObject.IsInstanceValid(body)
            ? body!.ToGlobal(new Vector2(end.X, end.Y))
            : null;
    }

    /// <summary>The document's links, for Build's hit-testing and the link view.</summary>
    public IReadOnlyList<SandboxLink> DocumentLinks =>
        SceneProgress?.ActiveSandbox.Links ?? (IReadOnlyList<SandboxLink>)Array.Empty<SandboxLink>();

    /// <summary>The document's signal wires, for the link view.</summary>
    public IReadOnlyList<SandboxWire> DocumentWires =>
        SceneProgress?.ActiveSandbox.Wires ?? (IReadOnlyList<SandboxWire>)Array.Empty<SandboxWire>();

    public Vector2 CanonicalToWorld(float x, float y)
    {
        Rect2 bounds = Boundaries.InnerBounds;
        return bounds.Position + new Vector2(bounds.Size.X * x, bounds.Size.Y * y);
    }

    /// <summary>Build's half-made link: a line from its first end to the pointer, or nothing.</summary>
    public void SetLinkPreview(Vector2? from, Vector2 to, bool valid)
    {
        // Clearing a preview that was never drawn needs no view; this also runs during teardown.
        if (from is null && (_linkView is null || !GodotObject.IsInstanceValid(_linkView)))
            return;
        EnsureLinkView();
        _linkView!.SetPreview(from, to, valid);
    }

    /// <summary>Asks the link view to redraw, e.g. while Build drags a linked part.</summary>
    public void RedrawBuiltLinks() => _linkView?.QueueRedraw();

    private void EnsureLinkView()
    {
        if (_linkView is not null && GodotObject.IsInstanceValid(_linkView))
            return;
        _linkView = new SandboxLinkView { Name = "SandboxLinkView", ZIndex = 5 };
        _linkView.Configure(this);
        AddChild(_linkView);
    }

    private void SpawnBuiltLink(SandboxLink link)
    {
        EnsureLinkView();
        if (!_builtParts.TryGetValue(link.A.PartId, out SandboxPartBody? a) || !GodotObject.IsInstanceValid(a))
            return;
        SandboxPartBody? b = null;
        if (!link.B.IsWorld &&
            (!_builtParts.TryGetValue(link.B.PartId, out b) || !GodotObject.IsInstanceValid(b)))
        {
            return;
        }

        var built = new BuiltLink(link, a!, b);
        Vector2 pivot = a!.ToGlobal(new Vector2(link.A.X, link.A.Y));
        switch (link.Kind)
        {
            case SandboxLinkKind.Hinge:
                AddPin(built, pivot, a, b ?? (PhysicsBody2D)AddRoomAnchor(built, pivot));
                break;
            case SandboxLinkKind.Weld when b is not null:
                // Two pins a little apart fix both where the parts meet and the angle between them.
                Vector2 toward = b.GlobalPosition - pivot;
                Vector2 along = toward.LengthSquared() > WeldPinSpacing * WeldPinSpacing
                    ? toward.Normalized() * WeldPinSpacing
                    : new Vector2(WeldPinSpacing, 0.0f);
                AddPin(built, pivot, a, b);
                AddPin(built, pivot + along, a, b);
                break;
            case SandboxLinkKind.Rope:
                // Ropes are a force on the fixed tick; nothing to add to the tree.
                break;
            default:
                return;
        }
        _builtLinks.Add(built);
    }

    private void AddPin(BuiltLink built, Vector2 pivot, PhysicsBody2D a, PhysicsBody2D b)
    {
        var pin = new PinJoint2D
        {
            Name = $"SandboxPin_{built.Link.LinkId.ToString()[..8]}_{built.Nodes.Count}",
            Softness = 0.0f,
            // Linked parts overlap where they join; letting them collide would push them apart.
            DisableCollision = true,
        };
        AddChild(pin);
        pin.GlobalPosition = pivot;
        pin.NodeA = pin.GetPathTo(a);
        pin.NodeB = pin.GetPathTo(b);
        built.Nodes.Add(pin);
    }

    /// <summary>A shapeless static anchor, so a hinge to the room pins to something that never moves.</summary>
    private StaticBody2D AddRoomAnchor(BuiltLink built, Vector2 at)
    {
        var anchor = new StaticBody2D
        {
            Name = $"SandboxAnchor_{built.Link.LinkId.ToString()[..8]}",
            CollisionLayer = 0,
            CollisionMask = 0,
        };
        AddChild(anchor);
        anchor.GlobalPosition = at;
        built.Nodes.Add(anchor);
        return anchor;
    }

    private void ClearBuiltLinks()
    {
        foreach (BuiltLink built in _builtLinks)
        {
            foreach (Node node in built.Nodes)
            {
                if (!GodotObject.IsInstanceValid(node))
                    continue;
                node.GetParent()?.RemoveChild(node);
                node.QueueFree();
            }
        }
        _builtLinks.Clear();
        _linkView?.QueueRedraw();
    }

    /// <summary>
    /// Rope pull on the routed fixed tick. A rope only pulls, and only past its length: slack when
    /// the ends are closer, a bounded damped spring when they are further, never a push.
    /// </summary>
    private void TickBuiltLinks(double delta)
    {
        if (_builtLinks.Count == 0 || delta <= 0.0)
            return;

        foreach (BuiltLink built in _builtLinks)
        {
            if (built.Link.Kind != SandboxLinkKind.Rope ||
                !GodotObject.IsInstanceValid(built.A) ||
                (built.B is not null && !GodotObject.IsInstanceValid(built.B)))
            {
                continue;
            }

            Vector2 held = built.A.ToGlobal(new Vector2(built.Link.A.X, built.Link.A.Y));
            Vector2 other = built.B is { } b
                ? b.ToGlobal(new Vector2(built.Link.B.X, built.Link.B.Y))
                : CanonicalToWorld(built.Link.B.X, built.Link.B.Y);
            Vector2 span = other - held;
            float distance = span.Length();
            if (distance <= built.Link.Length || distance < 0.001f)
                continue;

            Vector2 direction = span / distance;
            float massA = built.A.Freeze ? 0.0f : built.A.Mass;
            float massB = built.B is { Freeze: false } free ? free.Mass : 0.0f;
            float mass = massA > 0.0f && massB > 0.0f ? massA * massB / (massA + massB) : Math.Max(massA, massB);
            if (mass <= 0.0f)
                continue;

            // How fast the ends are separating. Damping adds to the pull while the rope is being
            // stretched and eases it while it recovers; subtracting it instead pumps energy into
            // every swing (a hung beam sagged 52 px below its rope's length before this was fixed).
            Vector2 relative = (built.B?.LinearVelocity ?? Vector2.Zero) - built.A.LinearVelocity;
            float separating = relative.Dot(direction);
            float stretch = distance - built.Link.Length;
            float pull = (stretch * RopeStiffness + separating * RopeDamping) * mass;
            if (pull <= 0.0f || !float.IsFinite(pull))
                continue;
            Vector2 force = direction * Math.Min(pull, RopeMaximumForce);

            built.A.ApplyForce(force, held - built.A.GlobalPosition);
            built.B?.ApplyForce(-force, other - built.B.GlobalPosition);
        }
        _linkView?.QueueRedraw();
    }
}

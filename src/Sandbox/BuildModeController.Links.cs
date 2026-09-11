using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>What a left click in the room does in Build.</summary>
public enum BuildTool
{
    Parts = 0,
    Rope = 1,
    Hinge = 2,
    Weld = 3,
    Wire = 4,
}

/// <summary>
/// Linking parts (NF-3): Rope, Hinge and Weld. A hinge or weld is one click on the spot where two
/// parts overlap, joining the top two there — the way an axle goes through a wheel and a beam. A
/// hinge on a single part pins it to the room, for pendulums and doors. A rope is two clicks: a
/// point on a part, then a point on another part or on the room. Nothing is stored unless the whole
/// link is valid, so a rejected link never leaves half a constraint in the room.
/// </summary>
public partial class BuildModeController
{
    private const float LinkHitRadius = 8.0f;

    private readonly Dictionary<BuildTool, Button> _toolButtons = [];
    private BuildTool _tool = BuildTool.Parts;
    private SandboxLinkEnd? _ropeStart;
    private SandboxPartId? _wireStart;

    public BuildTool Tool => _tool;

    public void SetTool(BuildTool tool)
    {
        _tool = tool;
        CancelPendingLink();
        foreach ((BuildTool key, Button button) in _toolButtons)
            button.SetPressedNoSignal(key == tool);
        SetStatus(tool switch
        {
            BuildTool.Rope => "Rope: click a point on a part, then another part or the room. Right-click cancels.",
            BuildTool.Hinge => "Hinge: click where two parts overlap. On one part, it pins that part to the room.",
            BuildTool.Weld => "Weld: click where two parts overlap to lock them together.",
            BuildTool.Wire => "Wire: click a device that sends (Button, Timer), then one that receives. Right-click a wire cuts it.",
            _ => "Parts: click empty space to place, click a part to select and drag it.",
        });
    }

    /// <summary>Hinges the top two parts under a point, or pins the only part there to the room.</summary>
    public SandboxLinkResult HingeAt(Vector2 world)
    {
        List<SandboxPartId> parts = _sandbox.PickBuiltPartsAt(world);
        if (parts.Count == 0)
            return Reject("A hinge goes on a part.");
        SandboxLinkEnd b = parts.Count >= 2 ? EndOn(parts[1], world) : RoomEnd(world);
        return CommitLink(SandboxLinkKind.Hinge, EndOn(parts[0], world), b, 0.0f);
    }

    public SandboxLinkResult WeldAt(Vector2 world)
    {
        List<SandboxPartId> parts = _sandbox.PickBuiltPartsAt(world);
        if (parts.Count < 2)
            return Reject("A weld joins two overlapping parts. To fix one part to the room, freeze it.");
        return CommitLink(SandboxLinkKind.Weld, EndOn(parts[0], world), EndOn(parts[1], world), 0.0f);
    }

    /// <summary>A rope from a point on a part to a point on another part, or on the room, pulled taut.</summary>
    public SandboxLinkResult RopeBetween(Vector2 from, Vector2 to)
    {
        if (!_sandbox.TryPickBuiltPart(from, out SandboxPartId start))
            return Reject("A rope starts on a part.");
        return FinishRope(EndOn(start, from), from, to);
    }

    /// <summary>
    /// Wires the device under <paramref name="from"/> to the device under <paramref name="to"/>:
    /// the first must send and the second receive, or nothing is stored.
    /// </summary>
    public SandboxWireResult WireBetween(Vector2 from, Vector2 to)
    {
        if (!TryPickDevice(from, SandboxPortDirection.Output, out SandboxPartId source))
            return RejectWire("A wire starts at a device that sends: a Button or a Timer.");
        return FinishWire(source, to);
    }

    /// <summary>Removes the link drawn under a point: a hinge or weld pin, or anywhere along a rope or wire.</summary>
    public bool RemoveLinkAt(Vector2 world)
    {
        if (RemoveWireAt(world))
            return true;

        SandboxLink? nearest = null;
        float best = LinkHitRadius;
        foreach (SandboxLink link in _sandbox.DocumentLinks)
        {
            if (_sandbox.LinkEndWorld(link.A) is not { } a || _sandbox.LinkEndWorld(link.B) is not { } b)
                continue;
            float distance = link.Kind == SandboxLinkKind.Rope
                ? DistanceToSegment(world, a, b)
                : world.DistanceTo(a);
            if (distance <= best)
            {
                best = distance;
                nearest = link;
            }
        }
        if (nearest is null)
            return false;

        _scenes.ActiveSandbox.RemoveLink(nearest.LinkId);
        _sandbox.RebuildBuiltLinks();
        RefreshProperties();
        SetStatus($"Removed a {nearest.Kind.ToString().ToLowerInvariant()}.");
        return true;
    }

    private bool HandleLinkInput(InputEvent @event)
    {
        Vector2 world = _sandbox.GetGlobalMousePosition();
        if (@event is InputEventMouseMotion)
        {
            UpdateLinkPreview(world);
            return false;
        }
        if (@event is not InputEventMouseButton { Pressed: true } button)
            return false;

        if (button.ButtonIndex == MouseButton.Right && (_ropeStart is not null || _wireStart is not null))
        {
            CancelPendingLink();
            SetStatus("Cancelled.");
            return true;
        }
        if (button.ButtonIndex != MouseButton.Left || !_sandbox.Boundaries.InnerBounds.HasPoint(world))
            return false;

        switch (_tool)
        {
            case BuildTool.Hinge:
                HingeAt(world);
                break;
            case BuildTool.Weld:
                WeldAt(world);
                break;
            case BuildTool.Rope when _ropeStart is null:
                if (_sandbox.TryPickBuiltPart(world, out SandboxPartId start))
                {
                    _ropeStart = EndOn(start, world);
                    SetStatus("Now click another part, or empty space to tie it to the room.");
                }
                else
                {
                    Reject("A rope starts on a part.");
                }
                break;
            case BuildTool.Rope:
                SandboxLinkEnd first = _ropeStart!.Value;
                if (_sandbox.LinkEndWorld(first) is { } from)
                    FinishRope(first, from, world);
                CancelPendingLink();
                break;
            case BuildTool.Wire when _wireStart is null:
                if (TryPickDevice(world, SandboxPortDirection.Output, out SandboxPartId source))
                {
                    _wireStart = source;
                    SetStatus("Now click the device it should set off.");
                }
                else
                {
                    RejectWire("A wire starts at a device that sends: a Button or a Timer.");
                }
                break;
            case BuildTool.Wire:
                FinishWire(_wireStart!.Value, world);
                CancelPendingLink();
                break;
        }
        UpdateLinkPreview(world);
        return true;
    }

    private SandboxLinkResult FinishRope(SandboxLinkEnd start, Vector2 from, Vector2 to)
    {
        List<SandboxPartId> targets = _sandbox.PickBuiltPartsAt(to);
        targets.Remove(start.PartId);
        SandboxLinkEnd end;
        if (targets.Count > 0)
            end = EndOn(targets[0], to);
        else if (_sandbox.PickBuiltPartsAt(to).Count > 0)
            return Reject("A rope needs a second part, or empty space to tie it to the room.");
        else
            end = RoomEnd(to);
        return CommitLink(SandboxLinkKind.Rope, start, end, from.DistanceTo(to));
    }

    private SandboxLinkResult CommitLink(SandboxLinkKind kind, SandboxLinkEnd a, SandboxLinkEnd b, float length)
    {
        SandboxLinkResult added = _scenes.ActiveSandbox.AddLink(kind, a, b, length);
        string name = kind.ToString().ToLowerInvariant();
        if (!added.Succeeded)
        {
            SetStatus(added.Status switch
            {
                SandboxLinkStatus.Duplicate => $"Those parts already have a {name}.",
                SandboxLinkStatus.LimitReached => $"This room already holds {SandboxDocument.MaximumLinks} links.",
                _ => added.Detail ?? $"Could not add the {name} ({added.Status}).",
            });
            return added;
        }

        _sandbox.RebuildBuiltLinks();
        RefreshProperties();
        SetStatus(b.IsWorld ? $"Added a {name} to the room." : $"Added a {name}.");
        return added;
    }

    private SandboxLinkResult Reject(string reason)
    {
        SetStatus(reason);
        return new SandboxLinkResult(SandboxLinkStatus.Invalid, null, reason);
    }

    private SandboxWireResult FinishWire(SandboxPartId source, Vector2 to)
    {
        if (!TryPickDevice(to, SandboxPortDirection.Input, out SandboxPartId target, except: source))
            return RejectWire("A wire ends at a device that receives: a Timer or a Lamp.");

        SandboxWireResult added = _scenes.ActiveSandbox.AddWire(source, SandboxDevices.Out, target, SandboxDevices.In);
        if (!added.Succeeded)
        {
            SetStatus(added.Status switch
            {
                SandboxLinkStatus.Duplicate => "Those two are already wired.",
                SandboxLinkStatus.LimitReached => $"This room already holds {SandboxDocument.MaximumWires} wires.",
                _ => added.Detail ?? $"Could not add the wire ({added.Status}).",
            });
            return added;
        }

        _sandbox.RedrawBuiltLinks();
        SetStatus("Wired.");
        return added;
    }

    private SandboxWireResult RejectWire(string reason)
    {
        SetStatus(reason);
        return new SandboxWireResult(SandboxLinkStatus.Invalid, null, reason);
    }

    private bool RemoveWireAt(Vector2 world)
    {
        SandboxWire? nearest = null;
        float best = LinkHitRadius;
        foreach (SandboxWire wire in _scenes.ActiveSandbox.Wires)
        {
            if (!_sandbox.BuiltParts.TryGetValue(wire.From, out SandboxPartBody? from) ||
                !_sandbox.BuiltParts.TryGetValue(wire.To, out SandboxPartBody? to))
            {
                continue;
            }
            float distance = DistanceToSegment(world, from.GlobalPosition, to.GlobalPosition);
            if (distance <= best)
            {
                best = distance;
                nearest = wire;
            }
        }
        if (nearest is null)
            return false;

        _scenes.ActiveSandbox.RemoveWire(nearest.WireId);
        _sandbox.RedrawBuiltLinks();
        SetStatus("Cut a wire.");
        return true;
    }

    /// <summary>The topmost device under a point that has a port facing that way.</summary>
    private bool TryPickDevice(
        Vector2 world, SandboxPortDirection direction, out SandboxPartId device, SandboxPartId except = default)
    {
        foreach (SandboxPartId candidate in _sandbox.PickBuiltPartsAt(world))
        {
            SandboxDeviceKind kind = _scenes.ActiveSandbox.DeviceOf(candidate);
            if (candidate != except &&
                SandboxDevices.HasPort(kind, direction == SandboxPortDirection.Output ? SandboxDevices.Out : SandboxDevices.In, direction))
            {
                device = candidate;
                return true;
            }
        }
        device = default;
        return false;
    }

    private void CancelPendingLink()
    {
        _ropeStart = null;
        _wireStart = null;
        _sandbox.SetLinkPreview(null, Vector2.Zero, true);
    }

    /// <summary>
    /// Shows where a link would go before it is made: a dashed line from a rope's first end, or a
    /// marker at the pointer that is blue where a hinge or weld can go and red where it cannot.
    /// </summary>
    private void UpdateLinkPreview(Vector2 world)
    {
        if (_tool == BuildTool.Parts)
        {
            _sandbox.SetLinkPreview(null, world, true);
            return;
        }
        if (_tool == BuildTool.Wire)
        {
            if (_wireStart is { } source && _sandbox.BuiltParts.TryGetValue(source, out SandboxPartBody? body))
            {
                _sandbox.SetLinkPreview(body.GlobalPosition, world,
                    TryPickDevice(world, SandboxPortDirection.Input, out _, except: source));
            }
            else
            {
                _sandbox.SetLinkPreview(world, world, TryPickDevice(world, SandboxPortDirection.Output, out _));
            }
            return;
        }
        int under = _sandbox.PickBuiltPartsAt(world).Count;
        if (_tool == BuildTool.Rope && _ropeStart is { } start && _sandbox.LinkEndWorld(start) is { } from)
        {
            _sandbox.SetLinkPreview(from, world, true);
            return;
        }
        bool valid = _tool switch
        {
            BuildTool.Hinge => under >= 1,
            BuildTool.Weld => under >= 2,
            _ => under >= 1,
        };
        _sandbox.SetLinkPreview(world, world, valid);
    }

    private SandboxLinkEnd EndOn(SandboxPartId partId, Vector2 world)
    {
        Vector2 local = _sandbox.BuiltParts[partId].ToLocal(world);
        return SandboxLinkEnd.OnPart(partId, local.X, local.Y);
    }

    private SandboxLinkEnd RoomEnd(Vector2 world)
    {
        CanonicalRoomPosition at = ToCanonical(world);
        return SandboxLinkEnd.World(at.X, at.Y);
    }

    private void BuildToolRow(VBoxContainer body)
    {
        var row = new HBoxContainer { Name = "BuildModeTools" };
        row.AddThemeConstantOverride("separation", Win98ThemeFactory.Gap);
        body.AddChild(row);
        var group = new ButtonGroup();
        foreach ((BuildTool tool, string label, string tip) in new[]
                 {
                     (BuildTool.Parts, "Parts", "1 — place, select and move parts."),
                     (BuildTool.Rope, "Rope", "2 — tie a part to another part or to the room."),
                     (BuildTool.Hinge, "Hinge", "3 — an axle where two parts overlap, or a pin to the room."),
                     (BuildTool.Weld, "Weld", "4 — lock two overlapping parts together."),
                     (BuildTool.Wire, "Wire", "5 — send a device's pulse to another device."),
                 })
        {
            var button = new Button
            {
                Name = $"BuildModeTool{label}",
                Text = label,
                TooltipText = tip,
                ToggleMode = true,
                ButtonGroup = group,
                ButtonPressed = tool == _tool,
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(72, 26),
            };
            button.Pressed += () => SetTool(tool);
            row.AddChild(button);
            _toolButtons[tool] = button;
        }
    }

    private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
    {
        Vector2 span = end - start;
        float lengthSquared = span.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return point.DistanceTo(start);
        float t = Math.Clamp((point - start).Dot(span) / lengthSquared, 0.0f, 1.0f);
        return point.DistanceTo(start + span * t);
    }
}

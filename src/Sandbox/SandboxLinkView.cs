using DesktopBuddy.App;
using DesktopBuddy.Domain.Sandbox;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// Draws the room's links from the document, against the parts as they stand: a rope as a line, a
/// hinge as a pin, a weld as a plate, a room anchor as a nail in the wall, and a signal wire as an
/// arrowed green line. Also draws Build's half-made link while the player is choosing its second end.
/// </summary>
public partial class SandboxLinkView : Node2D
{
    private static readonly Color RopeColor = new("8b5a2b");
    private static readonly Color PinFill = new("f0f0f0");
    private static readonly Color WeldFill = new("9aa6b4");
    private static readonly Color Ink = new("2a2118");
    private static readonly Color PreviewColor = new("000080");
    private static readonly Color SelectionOuter = new("000080");

    /// <summary>What each wire colour draws as; shared with the Build palette's preview.</summary>
    public static Color WireColorOf(SandboxWireColor color) => color switch
    {
        SandboxWireColor.Red => new Color("c0392b"),
        SandboxWireColor.Blue => new Color("2e6fd8"),
        SandboxWireColor.Yellow => new Color("e8b923"),
        SandboxWireColor.Purple => new Color("8e44ad"),
        SandboxWireColor.White => new Color("f2f2f2"),
        _ => new Color("1e8449"),
    };

    /// <summary>A rope's drawn thickness grows with its strength, so a weak one looks like string.</summary>
    public static float RopeWidthFor(float strength) => 1.5f + 2.5f * Mathf.Clamp(strength, 0.0f, 1.0f);

    private SandboxRoot _sandbox = null!;
    private Vector2? _previewFrom;
    private Vector2 _previewTo;
    private bool _previewValid;

    public void Configure(SandboxRoot sandbox) => _sandbox = sandbox;

    /// <summary>Shows or clears the line from a link's first end to the pointer.</summary>
    public void SetPreview(Vector2? from, Vector2 to, bool valid)
    {
        _previewFrom = from;
        _previewTo = to;
        _previewValid = valid;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_sandbox is null)
            return;

        foreach (SandboxLink link in _sandbox.DocumentLinks)
        {
            if (_sandbox.LinkEndWorld(link.A) is not { } a || _sandbox.LinkEndWorld(link.B) is not { } b)
                continue;
            // Build's selection, under the link so the link still reads on top of it.
            if (link.LinkId == _sandbox.HighlightedLink)
            {
                if (link.Kind == SandboxLinkKind.Rope)
                    DrawLine(a, b, SelectionOuter, RopeWidthFor(link.Strength) + 4.0f, true);
                else
                    DrawCircle(a, 9.0f, SelectionOuter, true, -1.0f, true);
            }
            switch (link.Kind)
            {
                case SandboxLinkKind.Rope:
                    float width = RopeWidthFor(link.Strength);
                    if (link.Elasticity >= 0.5f)
                        DrawDashedLine(a, b, RopeColor, width, 5.0f, true, true);   // a bungee's cord
                    else
                        DrawLine(a, b, RopeColor, width, true);
                    DrawCircle(a, 3.0f, RopeColor, true, -1.0f, true);
                    DrawCircle(b, 3.0f, RopeColor, true, -1.0f, true);
                    break;
                case SandboxLinkKind.Hinge:
                    DrawCircle(a, 5.0f, PinFill, true, -1.0f, true);
                    DrawArc(a, 5.0f, 0.0f, Mathf.Tau, 20, Ink, 1.5f, true);
                    DrawCircle(a, 1.5f, Ink, true, -1.0f, true);
                    break;
                case SandboxLinkKind.Weld:
                    var plate = new Rect2(a - new Vector2(5.0f, 5.0f), new Vector2(10.0f, 10.0f));
                    DrawRect(plate, WeldFill, filled: true);
                    DrawRect(plate, Ink, filled: false, 1.5f);
                    DrawLine(plate.Position, plate.End, Ink, 1.0f, true);
                    break;
            }
            if (link.B.IsWorld)
            {
                var nail = new Rect2(b - new Vector2(3.0f, 3.0f), new Vector2(6.0f, 6.0f));
                DrawRect(nail, Ink, filled: true);
            }
        }

        // Wires, in Build only: a line from the sending device to the receiving one, arrowed halfway along.
        foreach (SandboxWire wire in _sandbox.DocumentWires)
        {
            if (!_sandbox.WiresVisible)
                break;
            if (!_sandbox.BuiltParts.TryGetValue(wire.From, out SandboxPartBody? source) ||
                !_sandbox.BuiltParts.TryGetValue(wire.To, out SandboxPartBody? target))
            {
                continue;
            }
            Vector2 a = source.GlobalPosition;
            Vector2 b = target.GlobalPosition;
            Color color = WireColorOf(wire.Color);
            DrawLine(a, b, Ink, 3.5f, true);   // a dark core, so a white or yellow wire still reads
            DrawLine(a, b, color, 2.0f, true);
            if (a.DistanceSquaredTo(b) > 1.0f)
            {
                Vector2 along = (b - a).Normalized() * 6.0f;
                Vector2 middle = (a + b) * 0.5f;
                Vector2 side = along.Orthogonal() * 0.8f;
                DrawColoredPolygon([middle + along, middle - along + side, middle - along - side], color);
            }
        }

        if (_previewFrom is { } from)
        {
            Color color = _previewValid ? PreviewColor : new Color(0.7f, 0.1f, 0.1f);
            DrawDashedLine(from, _previewTo, color, 2.0f, 6.0f, true, true);
            DrawCircle(from, 4.0f, color, true, -1.0f, true);
        }
    }
}

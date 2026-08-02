using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

public enum CharacterDrawPrimitive
{
    FilledCircle,
    FilledPolygon,
    StrokePolyline,
}

/// <summary>
/// Immutable normalized draw command. Coordinates use x-right/y-up units centered on the
/// target decal. The painter control performs the sole normalized-to-pixel conversion.
/// </summary>
public sealed record CharacterDrawCommand(
    CharacterDrawPrimitive Primitive,
    Vector2[] Points,
    Color Color,
    float Radius = 0.0f,
    float Width = 0.0f)
{
    public static CharacterDrawCommand Circle(Vector2 center, float radius, Color color) =>
        new(CharacterDrawPrimitive.FilledCircle, [center], color, radius);

    public static CharacterDrawCommand Polygon(Vector2[] points, Color color) =>
        new(CharacterDrawPrimitive.FilledPolygon, points, color);

    public static CharacterDrawCommand Stroke(Vector2[] points, float width, Color color) =>
        new(CharacterDrawPrimitive.StrokePolyline, points, color, Width: width);
}

/// <summary>
/// One shared transform implementation for every feature family. Document offsets occupy
/// a bounded 35% of the decal half-extent; scale is applied before translation.
/// </summary>
public static class CharacterFeatureTransform
{
    public const float OffsetExtent = 0.35f;

    public static Vector2 Apply(
        Vector2 normalizedPoint,
        in NormalizedFeatureTransform transform)
    {
        float scale = (float)transform.Scale;
        return normalizedPoint * scale + new Vector2(
            (float)transform.OffsetX * OffsetExtent,
            (float)transform.OffsetY * OffsetExtent);
    }

    public static float ApplyLength(float normalizedLength, in NormalizedFeatureTransform transform) =>
        normalizedLength * (float)transform.Scale;

    public static Vector2[] Apply(
        IReadOnlyList<Vector2> normalizedPoints,
        in NormalizedFeatureTransform transform)
    {
        var result = new Vector2[normalizedPoints.Count];
        for (int index = 0; index < result.Length; index++)
            result[index] = Apply(normalizedPoints[index], transform);
        return result;
    }
}

public static class CharacterFeatureColors
{
    public static Color ToGodot(in Rgba32 color) => new(
        color.R / 255.0f,
        color.G / 255.0f,
        color.B / 255.0f,
        1.0f);
}

/// <summary>Executes normalized renderer commands on a face or accent viewport.</summary>
public partial class CharacterFeaturePainterControl : Control
{
    private IReadOnlyList<CharacterDrawCommand> _commands = Array.Empty<CharacterDrawCommand>();

    public IReadOnlyList<CharacterDrawCommand> Commands
    {
        get => _commands;
        set
        {
            _commands = value ?? Array.Empty<CharacterDrawCommand>();
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        float pixelsPerUnit = Mathf.Min(Size.X, Size.Y) * 0.5f;
        Vector2 center = Size * 0.5f;
        foreach (CharacterDrawCommand command in _commands)
        {
            switch (command.Primitive)
            {
                case CharacterDrawPrimitive.FilledCircle:
                    DrawCircle(ToPixel(command.Points[0], center, pixelsPerUnit),
                        command.Radius * pixelsPerUnit,
                        command.Color);
                    break;
                case CharacterDrawPrimitive.FilledPolygon:
                    DrawColoredPolygon(ToPixels(command.Points, center, pixelsPerUnit), command.Color);
                    break;
                case CharacterDrawPrimitive.StrokePolyline:
                    DrawPolyline(ToPixels(command.Points, center, pixelsPerUnit),
                        command.Color,
                        command.Width * pixelsPerUnit,
                        antialiased: true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private static Vector2 ToPixel(Vector2 normalized, Vector2 center, float scale) =>
        center + new Vector2(normalized.X * scale, -normalized.Y * scale);

    private static Vector2[] ToPixels(Vector2[] normalized, Vector2 center, float scale)
    {
        var result = new Vector2[normalized.Length];
        for (int index = 0; index < result.Length; index++)
            result[index] = ToPixel(normalized[index], center, scale);
        return result;
    }
}

internal static class CharacterGeometry
{
    public static Vector2[] Ellipse(Vector2 center, float radiusX, float radiusY, int segments = 20)
    {
        var points = new Vector2[segments];
        for (int index = 0; index < segments; index++)
        {
            float angle = Mathf.Tau * index / segments;
            points[index] = center + new Vector2(
                Mathf.Cos(angle) * radiusX,
                Mathf.Sin(angle) * radiusY);
        }
        return points;
    }

    public static Vector2[] Arc(
        Vector2 center,
        float radiusX,
        float radiusY,
        float startRadians,
        float endRadians,
        int segments = 12)
    {
        var points = new Vector2[segments + 1];
        for (int index = 0; index <= segments; index++)
        {
            float t = index / (float)segments;
            float angle = Mathf.Lerp(startRadians, endRadians, t);
            points[index] = center + new Vector2(
                Mathf.Cos(angle) * radiusX,
                Mathf.Sin(angle) * radiusY);
        }
        return points;
    }

    public static Vector2[] Rectangle(Vector2 center, Vector2 halfSize) =>
    [
        center + new Vector2(-halfSize.X, -halfSize.Y),
        center + new Vector2(halfSize.X, -halfSize.Y),
        center + new Vector2(halfSize.X, halfSize.Y),
        center + new Vector2(-halfSize.X, halfSize.Y),
    ];
}

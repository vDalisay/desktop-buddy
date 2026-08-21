using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

internal enum EyeVariant
{
    SoftOval,
    RoundDot,
    HorizontalLed,
    LashedOval,

    // Second wave (owner instruction 2026-08-21): the Mii-style expression set. Each one
    // changes only the open-eye silhouette; every reaction pose stays shared.
    SleepyHalf,
    AngrySlant,
    WideSparkle,
    NarrowSlit,
    BigRound,
}

internal sealed class ProceduralEyeRenderer : ICharacterEyeRenderer
{
    private readonly EyeVariant _variant;

    public ProceduralEyeRenderer(string featureId, EyeVariant variant)
    {
        FeatureId = featureId;
        _variant = variant;
    }

    public string FeatureId { get; }

    public IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        FaceEyePose pose,
        bool blinking,
        Vector2 pupilOffset,
        Color trustedOutlineColor)
    {
        var commands = new List<CharacterDrawCommand>(12);
        Color fill = CharacterFeatureColors.ToGodot(appearance.Color);
        NormalizedFeatureTransform transform = appearance.Transform;
        Vector2 left = new(-0.34f, 0.16f);
        Vector2 right = new(0.34f, 0.16f);

        if (blinking)
        {
            AddStroke(commands, CharacterGeometry.Arc(left, 0.12f, 0.07f, Mathf.Pi, Mathf.Tau),
                0.035f, fill, trustedOutlineColor, transform);
            AddStroke(commands, CharacterGeometry.Arc(right, 0.12f, 0.07f, Mathf.Pi, Mathf.Tau),
                0.035f, fill, trustedOutlineColor, transform);
            return commands;
        }

        switch (pose)
        {
            case FaceEyePose.HappyArc:
                AddStroke(commands, CharacterGeometry.Arc(left, 0.13f, 0.12f, 0.0f, Mathf.Pi),
                    0.035f, fill, trustedOutlineColor, transform);
                AddStroke(commands, CharacterGeometry.Arc(right, 0.13f, 0.12f, 0.0f, Mathf.Pi),
                    0.035f, fill, trustedOutlineColor, transform);
                return commands;
            case FaceEyePose.Scrunch:
                AddStroke(commands, [left + new Vector2(-0.11f, 0.08f), left, left + new Vector2(0.11f, -0.08f)],
                    0.035f, fill, trustedOutlineColor, transform);
                AddStroke(commands, [right + new Vector2(-0.11f, -0.08f), right, right + new Vector2(0.11f, 0.08f)],
                    0.035f, fill, trustedOutlineColor, transform);
                return commands;
            case FaceEyePose.Cross:
                AddCross(commands, left, fill, trustedOutlineColor, transform);
                AddCross(commands, right, fill, trustedOutlineColor, transform);
                return commands;
        }

        float height = pose == FaceEyePose.Narrow ? 0.065f : pose == FaceEyePose.Wide ? 0.18f : 0.13f;
        AddOpenEye(commands, left, height, fill, trustedOutlineColor, transform);
        AddOpenEye(commands, right, height, fill, trustedOutlineColor, transform);

        if (pose is FaceEyePose.Open or FaceEyePose.Narrow or FaceEyePose.Wide)
        {
            Vector2 boundedPupil = new(
                Mathf.Clamp(pupilOffset.X, -1.0f, 1.0f) * 0.035f,
                Mathf.Clamp(pupilOffset.Y, -1.0f, 1.0f) * 0.035f);
            if (HasSclera)
            {
                AddIris(commands, left, height, boundedPupil, fill, trustedOutlineColor, transform);
                AddIris(commands, right, height, boundedPupil, fill, trustedOutlineColor, transform);
            }
            else
            {
                AddCircle(commands, left + boundedPupil, 0.035f, trustedOutlineColor,
                    trustedOutlineColor, transform, outlineExpansion: 0.012f);
                AddCircle(commands, right + boundedPupil, 0.035f, trustedOutlineColor,
                    trustedOutlineColor, transform, outlineExpansion: 0.012f);
            }
        }

        return commands;
    }

    /// <summary>
    /// The white of the eye. Deliberately not pure white: a Mii's sclera is a warm off-white
    /// that sits against the face rather than punching a hole in it.
    /// </summary>
    private static readonly Color Sclera = new("fbf7f0");

    /// <summary>
    /// Which styles read as an eye you can see into and which are flat marks. An eye with a
    /// white, an iris and a catchlight is what makes a Mii's face read as a face rather than
    /// as two dark blobs (owner instruction 2026-08-21); a LED bar, a slit and a dot are
    /// deliberately solid and must stay that way or they stop being those styles.
    /// </summary>
    private bool HasSclera => _variant is
        EyeVariant.SoftOval or EyeVariant.LashedOval or EyeVariant.WideSparkle or
        EyeVariant.BigRound or EyeVariant.SleepyHalf or EyeVariant.AngrySlant;

    /// <summary>
    /// Iris, pupil and catchlight, in that order. The iris fills most of the eye the way a
    /// Mii's does — the white shows around it, not behind a small dot — and the catchlight is
    /// what makes it read as wet rather than printed.
    /// </summary>
    private static void AddIris(
        List<CharacterDrawCommand> commands,
        Vector2 center,
        float height,
        Vector2 look,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        float radius = Mathf.Max(0.048f, height * 0.62f);
        Vector2 iris = center + look;
        AddCircle(commands, iris, radius, fill, fill, transform, outlineExpansion: 0.0f);
        AddCircle(commands, iris, radius * 0.52f, outline, outline, transform, outlineExpansion: 0.0f);
        AddCircle(commands, iris + new Vector2(-radius * 0.32f, radius * 0.34f), radius * 0.28f,
            Sclera, Sclera, transform, outlineExpansion: 0.0f);
    }

    private void AddOpenEye(
        List<CharacterDrawCommand> commands,
        Vector2 center,
        float height,
        Color eyeColor,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        // For the styles that have a white, the authored colour becomes the lid and lash line
        // around a pale sclera; for the flat styles it stays the fill it always was.
        Color fill = HasSclera ? Sclera : eyeColor;
        if (HasSclera)
            outline = eyeColor;

        switch (_variant)
        {
            case EyeVariant.SoftOval:
                AddPolygon(commands, CharacterGeometry.Ellipse(center, 0.105f, height), fill, outline, transform);
                break;
            case EyeVariant.RoundDot:
                AddCircle(commands, center, Mathf.Max(0.075f, height * 0.72f), fill, outline, transform);
                break;
            case EyeVariant.HorizontalLed:
                AddPolygon(commands, CharacterGeometry.Rectangle(center,
                    new Vector2(0.14f, Mathf.Max(0.045f, height * 0.42f))), fill, outline, transform);
                break;
            case EyeVariant.LashedOval:
                AddPolygon(commands, CharacterGeometry.Ellipse(center, 0.105f, height), fill, outline, transform);
                // Two short lashes off the outer corner; the sign of the eye centre is the side.
                float lashSide = center.X < 0.0f ? -1.0f : 1.0f;
                Vector2 corner = center + new Vector2(lashSide * 0.09f, height * 0.5f);
                AddStroke(commands, [corner, corner + new Vector2(lashSide * 0.15f, 0.08f)],
                    0.026f, eyeColor, outline, transform);
                AddStroke(commands, [corner + new Vector2(lashSide * 0.02f, -0.06f), corner + new Vector2(lashSide * 0.17f, -0.02f)],
                    0.026f, eyeColor, outline, transform);
                break;
            case EyeVariant.SleepyHalf:
                // Lower half of an oval, capped by a heavy lid line across the top.
                AddPolygon(commands, CharacterGeometry.Ellipse(center - new Vector2(0.0f, height * 0.22f), 0.105f, height * 0.55f),
                    fill, outline, transform);
                AddStroke(commands, [center + new Vector2(-0.12f, height * 0.28f), center + new Vector2(0.12f, height * 0.28f)],
                    0.032f, eyeColor, outline, transform);
                break;
            case EyeVariant.AngrySlant:
                // A wedge: the inner corner drops, so the pair reads as a scowl. The sign of the
                // eye centre picks the side, exactly as the lashes do.
                float slantSide = center.X < 0.0f ? -1.0f : 1.0f;
                AddPolygon(commands,
                [
                    center + new Vector2(slantSide * -0.11f, height * 0.75f),
                    center + new Vector2(slantSide * 0.11f, height * 0.15f),
                    center + new Vector2(slantSide * 0.11f, -height * 0.85f),
                    center + new Vector2(slantSide * -0.11f, -height * 0.85f),
                ], fill, outline, transform);
                break;
            case EyeVariant.WideSparkle:
                AddPolygon(commands, CharacterGeometry.Ellipse(center, 0.12f, height * 1.25f), fill, outline, transform);
                break;
            case EyeVariant.NarrowSlit:
                AddPolygon(commands, CharacterGeometry.Rectangle(center, new Vector2(0.20f, 0.038f)),
                    fill, outline, transform);
                break;
            case EyeVariant.BigRound:
                AddCircle(commands, center, Mathf.Max(0.10f, height * 1.05f), fill, outline, transform);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static void AddCross(
        List<CharacterDrawCommand> commands,
        Vector2 center,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        AddStroke(commands, [center + new Vector2(-0.09f, 0.09f), center + new Vector2(0.09f, -0.09f)],
            0.035f, fill, outline, transform);
        AddStroke(commands, [center + new Vector2(-0.09f, -0.09f), center + new Vector2(0.09f, 0.09f)],
            0.035f, fill, outline, transform);
    }

    internal static void AddCircle(
        List<CharacterDrawCommand> commands,
        Vector2 center,
        float radius,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform,
        float outlineExpansion = 0.02f)
    {
        Vector2 transformed = CharacterFeatureTransform.Apply(center, transform);
        float scaledRadius = CharacterFeatureTransform.ApplyLength(radius, transform);
        commands.Add(CharacterDrawCommand.Circle(transformed, scaledRadius + outlineExpansion, outline));
        commands.Add(CharacterDrawCommand.Circle(transformed, scaledRadius, fill));
    }

    internal static void AddPolygon(
        List<CharacterDrawCommand> commands,
        Vector2[] points,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        Vector2[] transformed = CharacterFeatureTransform.Apply(points, transform);
        var closed = new Vector2[transformed.Length + 1];
        Array.Copy(transformed, closed, transformed.Length);
        closed[^1] = transformed[0];
        commands.Add(CharacterDrawCommand.Stroke(closed,
            CharacterFeatureTransform.ApplyLength(0.055f, transform), outline));
        commands.Add(CharacterDrawCommand.Polygon(transformed, fill));
    }

    internal static void AddStroke(
        List<CharacterDrawCommand> commands,
        Vector2[] points,
        float width,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        Vector2[] transformed = CharacterFeatureTransform.Apply(points, transform);
        commands.Add(CharacterDrawCommand.Stroke(transformed,
            CharacterFeatureTransform.ApplyLength(width + 0.035f, transform), outline));
        commands.Add(CharacterDrawCommand.Stroke(transformed,
            CharacterFeatureTransform.ApplyLength(width, transform), fill));
    }
}

internal enum BrowVariant
{
    SoftArc,
    Straight,
    Segmented,
    Bushy,
}

internal sealed class ProceduralBrowRenderer : ICharacterBrowRenderer
{
    private readonly BrowVariant _variant;

    public ProceduralBrowRenderer(string featureId, BrowVariant variant)
    {
        FeatureId = featureId;
        _variant = variant;
    }

    public string FeatureId { get; }

    public IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        FaceBrowPose pose,
        Color trustedOutlineColor)
    {
        if (pose == FaceBrowPose.None)
            return Array.Empty<CharacterDrawCommand>();

        var commands = new List<CharacterDrawCommand>(8);
        Color fill = CharacterFeatureColors.ToGodot(appearance.Color);
        NormalizedFeatureTransform transform = appearance.Transform;
        float y = pose == FaceBrowPose.Raised ? 0.52f : 0.44f;
        AddOne(commands, -0.34f, y, isLeft: true, pose, fill, trustedOutlineColor, transform);
        AddOne(commands, 0.34f, y, isLeft: false, pose, fill, trustedOutlineColor, transform);
        return commands;
    }

    private void AddOne(
        List<CharacterDrawCommand> commands,
        float x,
        float y,
        bool isLeft,
        FaceBrowPose pose,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        float innerSign = isLeft ? 1.0f : -1.0f;
        float tilt = pose switch
        {
            FaceBrowPose.AngledIn => -0.10f,
            FaceBrowPose.Worried => 0.10f,
            _ => 0.0f,
        };
        Vector2 outer = new(x - innerSign * 0.14f, y - tilt);
        Vector2 inner = new(x + innerSign * 0.14f, y + tilt);

        switch (_variant)
        {
            case BrowVariant.SoftArc:
                ProceduralEyeRenderer.AddStroke(commands,
                    CharacterGeometry.Arc(new Vector2(x, y - 0.02f), 0.15f, 0.07f,
                        0.12f * Mathf.Pi, 0.88f * Mathf.Pi),
                    0.026f, fill, outline, transform);
                break;
            case BrowVariant.Straight:
                ProceduralEyeRenderer.AddStroke(commands, [outer, inner],
                    0.03f, fill, outline, transform);
                break;
            case BrowVariant.Bushy:
                // Same arc family as SoftArc but heavy and wider, so it reads at tile size.
                ProceduralEyeRenderer.AddStroke(commands,
                    CharacterGeometry.Arc(new Vector2(x, y - 0.03f), 0.17f, 0.06f,
                        0.08f * Mathf.Pi, 0.92f * Mathf.Pi),
                    0.062f, fill, outline, transform);
                break;
            case BrowVariant.Segmented:
                Vector2 midpoint = outer.Lerp(inner, 0.5f);
                ProceduralEyeRenderer.AddStroke(commands, [outer, midpoint - new Vector2(innerSign * 0.025f, 0.0f)],
                    0.035f, fill, outline, transform);
                ProceduralEyeRenderer.AddStroke(commands, [midpoint + new Vector2(innerSign * 0.025f, 0.0f), inner],
                    0.035f, fill, outline, transform);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

internal enum MouthVariant
{
    Rounded,
    Pixel,
    Line,
    Oval,

    // Second wave (owner instruction 2026-08-21). Like the shipped four, these change only the
    // neutral/closed silhouette; reaction poses stay driven by FaceMouthPose.
    WideGrin,
    Frown,
    Smirk,
    OpenSmile,
    Pucker,
}

internal sealed class ProceduralMouthRenderer : ICharacterMouthRenderer
{
    private readonly MouthVariant _variant;

    public ProceduralMouthRenderer(string featureId, MouthVariant variant)
    {
        FeatureId = featureId;
        _variant = variant;
    }

    public string FeatureId { get; }

    public IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        FaceMouthPose pose,
        Color trustedOutlineColor)
    {
        var commands = new List<CharacterDrawCommand>(10);
        Color fill = CharacterFeatureColors.ToGodot(appearance.Color);
        NormalizedFeatureTransform transform = appearance.Transform;
        Vector2 center = new(0.0f, -0.35f);

        switch (pose)
        {
            case FaceMouthPose.Flat:
            case FaceMouthPose.ChewClosed:
                AddIdleSignature(commands, center, fill, trustedOutlineColor, transform);
                break;
            case FaceMouthPose.Smile:
                AddPath(commands, CharacterGeometry.Arc(center + new Vector2(0.0f, 0.06f), 0.18f, 0.12f, Mathf.Pi, Mathf.Tau), fill, trustedOutlineColor, transform);
                break;
            case FaceMouthPose.OpenSmile:
            case FaceMouthPose.ChewOpen:
                ProceduralEyeRenderer.AddPolygon(commands,
                    CharacterGeometry.Ellipse(center, 0.17f, pose == FaceMouthPose.ChewOpen ? 0.12f : 0.16f),
                    fill, trustedOutlineColor, transform);
                break;
            case FaceMouthPose.CatSmile:
                AddPath(commands, CharacterGeometry.Arc(center + new Vector2(-0.09f, 0.04f), 0.10f, 0.08f, Mathf.Pi, Mathf.Tau), fill, trustedOutlineColor, transform);
                AddPath(commands, CharacterGeometry.Arc(center + new Vector2(0.09f, 0.04f), 0.10f, 0.08f, Mathf.Pi, Mathf.Tau), fill, trustedOutlineColor, transform);
                break;
            case FaceMouthPose.Frown:
                AddPath(commands, CharacterGeometry.Arc(center - new Vector2(0.0f, 0.05f), 0.18f, 0.12f, 0.0f, Mathf.Pi), fill, trustedOutlineColor, transform);
                break;
            case FaceMouthPose.Squiggle:
                AddPath(commands,
                    [center + new Vector2(-0.16f, 0.0f), center + new Vector2(-0.06f, 0.06f), center + new Vector2(0.05f, -0.06f), center + new Vector2(0.16f, 0.0f)],
                    fill, trustedOutlineColor, transform);
                break;
            case FaceMouthPose.SmallO:
                ProceduralEyeRenderer.AddPolygon(commands, CharacterGeometry.Ellipse(center, 0.08f, 0.10f),
                    fill, trustedOutlineColor, transform);
                break;
            case FaceMouthPose.Slant:
                AddPath(commands, [center + new Vector2(-0.13f, -0.05f), center + new Vector2(0.13f, 0.05f)], fill, trustedOutlineColor, transform);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pose), pose, null);
        }

        return commands;
    }

    /// <summary>
    /// Neutral/closed mouths deliberately carry a strong family silhouette. User testing showed
    /// the old variants differed mostly by line width, so they were hard to distinguish in Studio.
    /// The three shipped families now read as rounded "3"-like, angular caret, and flat line while
    /// all reaction poses continue to be driven by the same semantic FaceMouthPose contract.
    /// </summary>
    private void AddIdleSignature(
        List<CharacterDrawCommand> commands,
        Vector2 center,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        switch (_variant)
        {
            case MouthVariant.Rounded:
                AddPath(commands,
                    CharacterGeometry.Arc(center + new Vector2(-0.07f, 0.035f), 0.085f, 0.065f, Mathf.Pi, Mathf.Tau),
                    fill, outline, transform);
                AddPath(commands,
                    CharacterGeometry.Arc(center + new Vector2(0.07f, 0.035f), 0.085f, 0.065f, Mathf.Pi, Mathf.Tau),
                    fill, outline, transform);
                break;
            case MouthVariant.Pixel:
                // Strong angular ^ silhouette; AddPath then applies the family pixel stepping.
                AddPath(commands,
                    [center + new Vector2(-0.15f, -0.045f), center + new Vector2(0.0f, 0.075f), center + new Vector2(0.15f, -0.045f)],
                    fill, outline, transform);
                break;
            case MouthVariant.Line:
                AddPath(commands,
                    [center + new Vector2(-0.16f, 0.0f), center + new Vector2(0.16f, 0.0f)],
                    fill, outline, transform);
                break;
            case MouthVariant.Oval:
                // Open "o" silhouette: a closed ellipse outline rather than a filled shape.
                AddPath(commands, Closed(CharacterGeometry.Ellipse(center, 0.10f, 0.12f)),
                    fill, outline, transform);
                break;
            case MouthVariant.WideGrin:
                AddPath(commands, CharacterGeometry.Arc(center + new Vector2(0.0f, 0.07f), 0.21f, 0.13f, Mathf.Pi, Mathf.Tau),
                    fill, outline, transform);
                break;
            case MouthVariant.Frown:
                AddPath(commands, CharacterGeometry.Arc(center - new Vector2(0.0f, 0.06f), 0.16f, 0.10f, 0.0f, Mathf.Pi),
                    fill, outline, transform);
                break;
            case MouthVariant.Smirk:
                // Lopsided on purpose: flat on the left, lifted on the right.
                AddPath(commands,
                    [center + new Vector2(-0.15f, -0.015f), center + new Vector2(0.05f, 0.0f), center + new Vector2(0.15f, 0.075f)],
                    fill, outline, transform);
                break;
            case MouthVariant.OpenSmile:
                ProceduralEyeRenderer.AddPolygon(commands, CharacterGeometry.Ellipse(center, 0.15f, 0.11f),
                    fill, outline, transform);
                break;
            case MouthVariant.Pucker:
                AddPath(commands, Closed(CharacterGeometry.Ellipse(center, 0.065f, 0.085f)),
                    fill, outline, transform);
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private void AddPath(
        List<CharacterDrawCommand> commands,
        Vector2[] path,
        Color fill,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        if (_variant == MouthVariant.Pixel)
        {
            Vector2[] stepped = Pixelate(path);
            ProceduralEyeRenderer.AddStroke(commands, stepped, 0.045f, fill, outline, transform);
            return;
        }

        float width = _variant == MouthVariant.Line ? 0.025f : 0.04f;
        ProceduralEyeRenderer.AddStroke(commands, path, width, fill, outline, transform);
    }

    private static Vector2[] Closed(Vector2[] path)
    {
        var closed = new Vector2[path.Length + 1];
        Array.Copy(path, closed, path.Length);
        closed[^1] = path[0];
        return closed;
    }

    private static Vector2[] Pixelate(Vector2[] path)
    {
        if (path.Length < 2)
            return path;
        var result = new List<Vector2>(path.Length * 2) { path[0] };
        for (int index = 1; index < path.Length; index++)
        {
            Vector2 previous = path[index - 1];
            Vector2 current = path[index];
            result.Add(new Vector2(current.X, previous.Y));
            result.Add(current);
        }
        return result.ToArray();
    }
}

internal enum AccentVariant
{
    None,
    Panel,
    Chevron,
    Bolts,
}

internal sealed class ProceduralAccentRenderer : ICharacterAccentRenderer
{
    private readonly AccentVariant _variant;

    public ProceduralAccentRenderer(string featureId, AccentVariant variant)
    {
        FeatureId = featureId;
        _variant = variant;
    }

    public string FeatureId { get; }

    public IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        Color trustedOutlineColor)
    {
        if (_variant == AccentVariant.None)
            return Array.Empty<CharacterDrawCommand>();

        var commands = new List<CharacterDrawCommand>(8);
        Color fill = CharacterFeatureColors.ToGodot(appearance.Color);
        NormalizedFeatureTransform transform = appearance.Transform;
        switch (_variant)
        {
            case AccentVariant.Panel:
                ProceduralEyeRenderer.AddPolygon(commands,
                    CharacterGeometry.Rectangle(Vector2.Zero, new Vector2(0.42f, 0.32f)),
                    fill, trustedOutlineColor, transform);
                break;
            case AccentVariant.Chevron:
                ProceduralEyeRenderer.AddStroke(commands,
                    [new Vector2(-0.42f, 0.22f), new Vector2(0.0f, -0.18f), new Vector2(0.42f, 0.22f)],
                    0.09f, fill, trustedOutlineColor, transform);
                break;
            case AccentVariant.Bolts:
                foreach (Vector2 point in new[]
                {
                    new Vector2(-0.30f, 0.22f), new Vector2(0.30f, 0.22f),
                    new Vector2(-0.30f, -0.22f), new Vector2(0.30f, -0.22f),
                })
                {
                    ProceduralEyeRenderer.AddCircle(commands, point, 0.08f,
                        fill, trustedOutlineColor, transform);
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
        return commands;
    }
}

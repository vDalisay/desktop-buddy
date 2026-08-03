using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Painting;

namespace DesktopBuddy.Domain.Characters;

public sealed record CharacterPaintManifest
{
    public static CharacterPaintManifest Empty { get; } = new();

    public string? Head { get; init; }
    public string? Torso { get; init; }
    public string? LeftHand { get; init; }
    public string? RightHand { get; init; }
    public string? LeftFoot { get; init; }
    public string? RightFoot { get; init; }

    public string? PathFor(PaintPart part) => part switch
    {
        PaintPart.Head => Head,
        PaintPart.Torso => Torso,
        PaintPart.LeftHand => LeftHand,
        PaintPart.RightHand => RightHand,
        PaintPart.LeftFoot => LeftFoot,
        PaintPart.RightFoot => RightFoot,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown paint part."),
    };

    public CharacterPaintManifest WithPath(PaintPart part, string? path) => part switch
    {
        PaintPart.Head => this with { Head = path },
        PaintPart.Torso => this with { Torso = path },
        PaintPart.LeftHand => this with { LeftHand = path },
        PaintPart.RightHand => this with { RightHand = path },
        PaintPart.LeftFoot => this with { LeftFoot = path },
        PaintPart.RightFoot => this with { RightFoot = path },
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown paint part."),
    };

    public IEnumerable<(PaintPart Part, string Path)> Declared()
    {
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
        {
            string? path = PathFor(part);
            if (!string.IsNullOrEmpty(path))
                yield return (part, path);
        }
    }

    public static CharacterPaintManifest ForNonBlank(IReadOnlyCollection<PaintPart> parts)
    {
        CharacterPaintManifest manifest = Empty;
        foreach (PaintPart part in parts)
            manifest = manifest.WithPath(part, PaintPolicy.WhitelistedPaths[part]);
        return manifest;
    }
}

using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Painting;

namespace DesktopBuddy.Domain.Characters;

public sealed record CharacterPaintManifest
{
    public static CharacterPaintManifest Empty { get; } = new();

    public string? Head { get; set; }
    public string? Torso { get; set; }
    public string? LeftHand { get; set; }
    public string? RightHand { get; set; }
    public string? LeftFoot { get; set; }
    public string? RightFoot { get; set; }

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

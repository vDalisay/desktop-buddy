using System;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// Immutable six-circle anatomy and structural-link graph. The checked-in lab
/// asset is provisional tuning data until the Milestone 1 acceptance gate.
/// </summary>
[GlobalClass]
public partial class PuppetRigProfile : GameResource
{
    public const int RequiredPartCount = 6;

    [Export] public Godot.Collections.Array<PuppetPartDefinition> Parts { get; set; } = new();
    [Export] public Godot.Collections.Array<PuppetLinkDefinition> Links { get; set; } = new();
    /// <summary>Passive spring gain while a grab lifts the entire rig off support.</summary>
    [Export(PropertyHint.Range, "1,12,0.1")]
    public float AirborneGrabStiffnessMultiplier { get; set; } = 5.0f;
    [Export(PropertyHint.Range, "1,8,0.1")]
    public float AirborneGrabDampingMultiplier { get; set; } = 2.0f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (Parts.Count != RequiredPartCount)
        {
            errors.Add($"expected exactly {RequiredPartCount} parts; found {Parts.Count}");
        }

        Span<bool> seen = stackalloc bool[RequiredPartCount];
        for (int index = 0; index < Parts.Count; index++)
        {
            PuppetPartDefinition? part = Parts[index];
            if (part is null)
            {
                errors.Add($"part[{index}] is missing");
                continue;
            }

            int id = (int)part.PartId;
            if (id < 0 || id >= RequiredPartCount)
            {
                errors.Add($"part[{index}] has invalid id {id}");
            }
            else if (seen[id])
            {
                errors.Add($"duplicate part id {part.PartId}");
            }
            else
            {
                seen[id] = true;
            }

            if (!float.IsFinite(part.Radius) || part.Radius <= 0.0f)
            {
                errors.Add($"{part.PartId} radius must be finite and positive");
            }

            if (!float.IsFinite(part.Mass) || part.Mass <= 0.0f)
            {
                errors.Add($"{part.PartId} mass must be finite and positive");
            }

            if (!IsFiniteNonNegative(part.LinearDamp) || !IsFiniteNonNegative(part.AngularDamp))
            {
                errors.Add($"{part.PartId} damping must be finite and non-negative");
            }

            if (!part.RestPosition.IsFinite())
            {
                errors.Add($"{part.PartId} rest position must be finite");
            }
        }

        for (int id = 0; id < seen.Length; id++)
        {
            if (!seen[id])
            {
                errors.Add($"missing part id {(BuddyPartId)id}");
            }
        }

        if (Links.Count == 0)
        {
            errors.Add("at least one structural link is required");
        }

        if (!float.IsFinite(AirborneGrabStiffnessMultiplier) ||
            AirborneGrabStiffnessMultiplier < 1.0f ||
            !float.IsFinite(AirborneGrabDampingMultiplier) ||
            AirborneGrabDampingMultiplier < 1.0f)
        {
            errors.Add("airborne grab structural multipliers must be finite and at least one");
        }

        var linkIds = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < Links.Count; index++)
        {
            PuppetLinkDefinition? link = Links[index];
            if (link is null)
            {
                errors.Add($"link[{index}] is missing");
                continue;
            }

            string linkId = link.LinkId.ToString();
            if (string.IsNullOrWhiteSpace(linkId))
            {
                errors.Add($"link[{index}] id is empty");
            }
            else if (!linkIds.Add(linkId))
            {
                errors.Add($"duplicate link id {linkId}");
            }

            if (link.PartA == link.PartB)
            {
                errors.Add($"{linkId} connects a part to itself");
            }

            if (!IsValidPartId(link.PartA) || !IsValidPartId(link.PartB))
            {
                errors.Add($"{linkId} references an invalid part id");
            }

            if (!link.LocalAnchorA.IsFinite() || !link.LocalAnchorB.IsFinite() || !link.RestOffset.IsFinite())
            {
                errors.Add($"{linkId} vectors must be finite");
            }

            if (!IsFinitePositive(link.Stiffness) || !IsFiniteNonNegative(link.Damping) ||
                !IsFinitePositive(link.MaximumDistance) || !IsFiniteNonNegative(link.LimitStiffness) ||
                !IsFinitePositive(link.MaximumForce))
            {
                errors.Add($"{linkId} force coefficients are invalid");
            }

            if (link.RestOffset.Length() > link.MaximumDistance)
            {
                errors.Add($"{linkId} rest offset exceeds maximum distance");
            }
        }

        return errors;
    }

    public PuppetPartDefinition? FindPart(BuddyPartId id)
    {
        for (int index = 0; index < Parts.Count; index++)
        {
            PuppetPartDefinition? part = Parts[index];
            if (part is not null && part.PartId == id)
            {
                return part;
            }
        }

        return null;
    }

    private static bool IsFinitePositive(float value) => float.IsFinite(value) && value > 0.0f;
    private static bool IsFiniteNonNegative(float value) => float.IsFinite(value) && value >= 0.0f;
    private static bool IsValidPartId(BuddyPartId id) =>
        (int)id >= 0 && (int)id < RequiredPartCount;
}

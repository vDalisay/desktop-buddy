using System;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Immutable visual-only definition for the buddy. This is the future character-editor
/// seam: visual customization may replace this profile but never changes rig or drive tuning.
/// </summary>
[GlobalClass]
public partial class BuddyVisualProfile : GameResource
{
    public const float MinimumCapsuleHeightScale = 2.0f;
    public const float MaximumCapsuleHeightScale = 3.0f;

    [Export] public Godot.Collections.Array<PartVisualDefinition> Parts { get; set; } = new();
    [Export] public Godot.Collections.Array<ConnectorVisualDefinition> Connectors { get; set; } = new();
    [Export(PropertyHint.Range, "2,3,0.01")]
    public float CapsuleHeightScale { get; set; } = 2.5f;
    [Export(PropertyHint.Range, "0.01,64,0.01,or_greater")]
    public float ConnectorMinimumLength { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "1,128,1,or_greater")]
    public int FaceTextSize { get; set; } = 14;
    [Export(PropertyHint.Range, "0.001,4,0.001,or_greater")]
    public float FacePixelSize { get; set; } = 1.0f;
    [Export] public Color FaceColor { get; set; } = new("183042");
    [Export(PropertyHint.Range, "0.001,16,0.001,or_greater")]
    public float FaceDepthEpsilon { get; set; } = 0.1f;

    public override Godot.Collections.Array<string> Validate()
    {
        var errors = new Godot.Collections.Array<string>();
        if (Parts.Count != PuppetRigProfile.RequiredPartCount)
        {
            errors.Add($"expected exactly {PuppetRigProfile.RequiredPartCount} part visuals; found {Parts.Count}");
        }

        Span<bool> seen = stackalloc bool[PuppetRigProfile.RequiredPartCount];
        for (int index = 0; index < Parts.Count; index++)
        {
            PartVisualDefinition? part = Parts[index];
            if (part is null)
            {
                errors.Add($"part[{index}] is missing");
                continue;
            }

            int id = (int)part.PartId;
            if (!IsValidPartId(part.PartId))
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

            if (!IsFiniteColor(part.Color))
            {
                errors.Add($"{part.PartId} color must be finite");
            }

            if (!IsFinitePositive(part.MeshRadiusScale))
            {
                errors.Add($"{part.PartId} mesh radius scale must be finite and positive");
            }

            if (!float.IsFinite(part.DepthOffset))
            {
                errors.Add($"{part.PartId} depth offset must be finite");
            }

            if (!Enum.IsDefined(part.RotationPolicy))
            {
                errors.Add($"{part.PartId} rotation policy is invalid");
            }

            if (!IsFinitePositive(part.VelocitySmoothing) ||
                !IsFiniteNonNegative(part.VelocitySpeedDeadband))
            {
                errors.Add($"{part.PartId} velocity orientation values are invalid");
            }
        }

        for (int id = 0; id < seen.Length; id++)
        {
            if (!seen[id])
            {
                errors.Add($"missing part id {(BuddyPartId)id}");
            }
        }

        if (!float.IsFinite(CapsuleHeightScale) ||
            CapsuleHeightScale < MinimumCapsuleHeightScale ||
            CapsuleHeightScale > MaximumCapsuleHeightScale)
        {
            errors.Add($"capsule height scale must be within {MinimumCapsuleHeightScale:0.0}–{MaximumCapsuleHeightScale:0.0}");
        }

        if (Connectors.Count == 0)
        {
            errors.Add("at least one connector is required");
        }

        for (int index = 0; index < Connectors.Count; index++)
        {
            ConnectorVisualDefinition? connector = Connectors[index];
            if (connector is null)
            {
                errors.Add($"connector[{index}] is missing");
                continue;
            }

            if (!IsValidPartId(connector.PartA) || !IsValidPartId(connector.PartB))
            {
                errors.Add($"connector[{index}] references an invalid part id");
            }
            else if (connector.PartA == connector.PartB)
            {
                errors.Add($"connector[{index}] connects a part to itself");
            }

            if (!IsFinitePositive(connector.Radius))
            {
                errors.Add($"connector[{index}] radius must be finite and positive");
            }

            if (!IsFiniteColor(connector.Color) || !float.IsFinite(connector.DepthOffset))
            {
                errors.Add($"connector[{index}] color and depth offset must be finite");
            }
        }

        if (!IsFinitePositive(ConnectorMinimumLength))
        {
            errors.Add("connector minimum length must be finite and positive");
        }

        if (FaceTextSize <= 0 || !IsFinitePositive(FacePixelSize) ||
            !IsFiniteColor(FaceColor) || !IsFinitePositive(FaceDepthEpsilon))
        {
            errors.Add("face text size, pixel size, color, and depth epsilon are invalid");
        }

        return errors;
    }

    public PartVisualDefinition? FindPart(BuddyPartId id)
    {
        for (int index = 0; index < Parts.Count; index++)
        {
            PartVisualDefinition? part = Parts[index];
            if (part is not null && part.PartId == id)
            {
                return part;
            }
        }

        return null;
    }

    private static bool IsValidPartId(BuddyPartId id) =>
        (int)id >= 0 && (int)id < PuppetRigProfile.RequiredPartCount;
    private static bool IsFinitePositive(float value) => float.IsFinite(value) && value > 0.0f;
    private static bool IsFiniteNonNegative(float value) => float.IsFinite(value) && value >= 0.0f;
    private static bool IsFiniteColor(Color color) =>
        float.IsFinite(color.R) && float.IsFinite(color.G) &&
        float.IsFinite(color.B) && float.IsFinite(color.A);
}

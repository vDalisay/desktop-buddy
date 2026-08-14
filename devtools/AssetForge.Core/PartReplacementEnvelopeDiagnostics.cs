using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

public readonly record struct PartReplacementEnvelopeDiagnostic(
    float MaximumVisualRadius,
    float PhysicsRadius,
    float WarningThreshold,
    bool SubstantiallyExceedsPhysicsEnvelope,
    string Summary);

/// <summary>
/// Developer-only visual warning for part replacements. Replacement coordinates are authored in
/// trusted-part-radius units, so a unit radius is the unchanged gameplay envelope. This diagnostic
/// never edits geometry or physics; it only reports when visual art extends far beyond that envelope.
/// </summary>
public static class PartReplacementEnvelopeDiagnostics
{
    public const float PhysicsRadius = 1.0f;
    public const float WarningMultiplier = 1.25f;

    public static PartReplacementEnvelopeDiagnostic Analyze(CanonicalMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        float maximum = 0f;
        foreach (Vector3 position in mesh.Positions)
        {
            // The authoritative 2D collision lives in the Buddy's XY presentation plane. Visual Z
            // depth is intentionally author-controlled and must not cause a collision-envelope warning.
            float planar = MathF.Sqrt(position.X * position.X + position.Y * position.Y);
            maximum = MathF.Max(maximum, planar);
        }

        float threshold = PhysicsRadius * WarningMultiplier;
        bool warning = maximum > threshold;
        string summary = warning
            ? $"WARNING: Visual reaches {maximum:0.00}× the trusted part radius, beyond the {threshold:0.00}× presentation warning threshold. Physics remains unchanged."
            : $"Visual reaches {maximum:0.00}× the trusted part radius. Physics remains unchanged.";
        return new PartReplacementEnvelopeDiagnostic(
            maximum,
            PhysicsRadius,
            threshold,
            warning,
            summary);
    }
}

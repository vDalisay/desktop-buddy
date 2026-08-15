namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Non-blocking authoring diagnostics. These warnings never rewrite geometry and never enter the
/// canonical asset hash; they only help a developer notice suspicious-but-valid input before export.
/// Invalid/trust-breaking content remains the responsibility of recipe/generator/export validation.
/// </summary>
public static class AssetForgeGenerationWarnings
{
    public static IReadOnlyList<string> Analyze(GeneratedAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var warnings = new List<string>();
        AssetRecipe recipe = asset.Recipe;
        MaskDiagnostics mask = asset.Diagnostics;

        if (mask.Components >= 8)
            warnings.Add($"WARNING: {mask.Components} disconnected visible shapes detected. Confirm that small islands are intentional.");
        else if (mask.Components > 1)
            warnings.Add($"NOTICE: {mask.Components} disconnected visible shapes detected.");

        if (recipe.Category == AssetCategory.Glasses && mask.Holes < 2)
            warnings.Add("WARNING: Glasses contain fewer than two detected interior openings. Confirm both lens openings remain open.");

        if (recipe.Category is AssetCategory.TorsoShape or AssetCategory.FootShape)
        {
            PartReplacementEnvelopeDiagnostic envelope = PartReplacementEnvelopeDiagnostics.Analyze(asset.Mesh);
            if (envelope.SubstantiallyExceedsPhysicsEnvelope) warnings.Add(envelope.Summary);
        }

        if (recipe.AssetFamily == AssetFamily.Environment && recipe.Geometry.Depth > 1.0)
            warnings.Add($"WARNING: {recipe.Category} visual depth exceeds its authored reference height. Confirm this exaggerated 2.5D depth is intentional.");

        int budget = recipe.AssetFamily == AssetFamily.Environment ? 45_000 :
            recipe.Category is AssetCategory.TorsoShape or AssetCategory.FootShape ? 20_000 : 30_000;
        if (asset.TriangleCount > budget)
            warnings.Add($"WARNING: Runtime mesh has {asset.TriangleCount:N0} triangles, above the recommended {budget:N0} budget for this category.");

        return warnings;
    }
}

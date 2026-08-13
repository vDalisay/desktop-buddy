using System.Numerics;

namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Canonical 1024x1024 authoring projection for glasses@2.
///
/// The artist draws directly over a Buddy-head guide. Source pixels are mapped into head-radius
/// units without auto-centering or auto-scaling, so placement and dimensions in the template are
/// meaningful and match the Asset Forge preview/runtime attachment.
/// </summary>
public static class GlassesTemplateSpace
{
    public const int CanvasSize = AssetForgeGenerator.SourceSize;
    public const float HeadCenterX = 512.0f;
    public const float HeadCenterY = 512.0f;
    public const float HeadRadiusPixels = 360.0f;

    // Visual guide only. The generator does not snap to these values: what the artist draws wins.
    public const float RecommendedEyeYHeadUnits = 0.18f;
    public const float RecommendedEyeXHeadUnits = 0.42f;

    public static float RecommendedEyeLineY =>
        HeadCenterY - RecommendedEyeYHeadUnits * HeadRadiusPixels;

    public static float LeftEyeCenterX =>
        HeadCenterX - RecommendedEyeXHeadUnits * HeadRadiusPixels;

    public static float RightEyeCenterX =>
        HeadCenterX + RecommendedEyeXHeadUnits * HeadRadiusPixels;

    public static Vector2 SourcePixelToHead(Vector2 sourcePixel) => new(
        (sourcePixel.X - HeadCenterX) / HeadRadiusPixels,
        (HeadCenterY - sourcePixel.Y) / HeadRadiusPixels);

    public static Vector2 GridPointToHead(MaskGrid grid, Vector2 gridPoint)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return SourcePixelToHead(GridPointToSourcePixel(grid, gridPoint));
    }

    public static Vector2 GridPointToSourcePixel(MaskGrid grid, Vector2 gridPoint)
    {
        ArgumentNullException.ThrowIfNull(grid);
        return new Vector2(
            gridPoint.X * CanvasSize / grid.Width,
            gridPoint.Y * CanvasSize / grid.Height);
    }

    public static Vector2 HeadToSourcePixel(Vector2 headPoint) => new(
        HeadCenterX + headPoint.X * HeadRadiusPixels,
        HeadCenterY - headPoint.Y * HeadRadiusPixels);
}

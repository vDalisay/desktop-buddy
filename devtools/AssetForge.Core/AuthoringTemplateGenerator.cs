namespace DesktopBuddy.AssetForge.Core;

/// <summary>
/// Compatibility seam retained for the existing Asset Forge UI. Glasses@2 uses the canonical
/// Buddy-head coloring template so source pixels correspond directly to head-relative placement.
/// </summary>
public static class AuthoringTemplateGenerator
{
    public static byte[] CreateGlassesTemplatePng() => BuddyHeadTemplateGenerator.CreatePng();
}

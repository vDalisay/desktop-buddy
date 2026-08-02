namespace DesktopBuddy.Domain.Characters;

public readonly record struct NormalizedFeatureTransform(
    double OffsetX,
    double OffsetY,
    double Scale)
{
    public const double MinimumOffset = -1.0;
    public const double MaximumOffset = 1.0;
    public const double MinimumScale = 0.75;
    public const double MaximumScale = 1.25;

    public static NormalizedFeatureTransform Identity { get; } = new(0.0, 0.0, 1.0);
}

using DesktopBuddy.Domain.Characters;

namespace DesktopBuddy.Domain.Characters;

internal static class Rgba32EditorExtensions
{
    public static string ToHex(this Rgba32 color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

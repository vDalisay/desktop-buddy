using System.Globalization;
using DesktopBuddy.Buddy.Presentation3D.Shared;
using Godot;

namespace DesktopBuddy.AssetForge;

public readonly record struct TrustedBuddyPreviewProfile(
    float HeadRadius,
    float FaceDepthEpsilon,
    Color HeadColor,
    BuddySharedLook Look);

/// <summary>Reads the synchronized authoritative Buddy *.tres values as developer data.</summary>
public static class TrustedBuddyProfileReader
{
    public static TrustedBuddyPreviewProfile Load()
    {
        string root = ProjectSettings.GlobalizePath("res://.generated/profiles");
        string rig = Read(root, "lab_puppet_rig.tres");
        string visual = Read(root, "lab_buddy_visual.tres");
        string look = Read(root, "lab_buddy_look.tres");

        string rigHead = Block(rig, "[sub_resource type=\"Resource\" id=\"PartHead\"]");
        string visualHead = Block(visual, "[sub_resource type=\"Resource\" id=\"PartHead\"]");
        float headRadius = Float(Value(rigHead, "Radius"));
        float epsilon = Float(Value(visual, "FaceDepthEpsilon"));
        Color headColor = ColorValue(Value(visualHead, "Color"));
        var shared = new BuddySharedLook(
            (BaseMaterial3D.DiffuseModeEnum)Int(Value(look, "DiffuseMode")),
            (BaseMaterial3D.SpecularModeEnum)Int(Value(look, "SpecularMode")),
            Float(Value(look, "Specular")),
            Float(Value(look, "Roughness")),
            ColorValue(Value(look, "KeyColor")),
            Float(Value(look, "KeyEnergy")),
            VectorValue(Value(look, "KeyEulerDegrees")),
            ColorValue(Value(look, "FillColor")),
            Float(Value(look, "FillEnergy")),
            VectorValue(Value(look, "FillEulerDegrees")),
            Bool(Value(look, "ShadowsEnabled")),
            ColorValue(Value(look, "OutlineColor")),
            Float(Value(look, "OutlineGrowAmount")));
        return new TrustedBuddyPreviewProfile(headRadius, epsilon, headColor, shared);
    }

    private static string Read(string root, string name)
    {
        string path = Path.Combine(root, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Trusted Buddy preview data is missing: {path}. Run devtools/AssetForge/build_asset_forge.bat.");
        return File.ReadAllText(path);
    }

    private static string Block(string text, string marker)
    {
        int start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new FormatException($"Missing trusted profile block {marker}.");
        int next = text.IndexOf("\n[sub_resource", start + marker.Length, StringComparison.Ordinal);
        if (next < 0) next = text.IndexOf("\n[resource]", start + marker.Length, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    private static string Value(string text, string key)
    {
        string prefix = key + " = ";
        foreach (string line in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return line[prefix.Length..].Trim();
        throw new FormatException($"Missing trusted profile value {key}.");
    }

    private static int Int(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
    private static float Float(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static bool Bool(string value) => bool.Parse(value);

    private static Color ColorValue(string value)
    {
        string inside = Between(value, "Color(", ")");
        float[] values = inside.Split(',').Select(static item => Float(item.Trim())).ToArray();
        if (values.Length is < 3 or > 4) throw new FormatException("Invalid Color value.");
        return new Color(values[0], values[1], values[2], values.Length == 4 ? values[3] : 1f);
    }

    private static Vector3 VectorValue(string value)
    {
        string inside = Between(value, "Vector3(", ")");
        float[] values = inside.Split(',').Select(static item => Float(item.Trim())).ToArray();
        if (values.Length != 3) throw new FormatException("Invalid Vector3 value.");
        return new Vector3(values[0], values[1], values[2]);
    }

    private static string Between(string value, string prefix, string suffix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || !value.EndsWith(suffix, StringComparison.Ordinal))
            throw new FormatException($"Invalid trusted resource value '{value}'.");
        return value[prefix.Length..^suffix.Length];
    }
}

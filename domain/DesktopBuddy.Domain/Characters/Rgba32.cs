using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopBuddy.Domain.Characters;

[JsonConverter(typeof(Rgba32JsonConverter))]
public readonly record struct Rgba32(byte R, byte G, byte B)
{
    public byte A => byte.MaxValue;

    public static Rgba32 Parse(string value)
    {
        if (!TryParse(value, out Rgba32 color))
            throw new FormatException("Color must use opaque #RRGGBB syntax.");
        return color;
    }

    public static bool TryParse(string? value, out Rgba32 color)
    {
        color = default;
        if (value is null || value.Length != 7 || value[0] != '#')
            return false;

        if (!byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red) ||
            !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green) ||
            !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
        {
            return false;
        }

        color = new Rgba32(red, green, blue);
        return true;
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

public sealed class Rgba32JsonConverter : JsonConverter<Rgba32>
{
    public override Rgba32 Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Color must be a #RRGGBB string.");

        string? value = reader.GetString();
        if (!Rgba32.TryParse(value, out Rgba32 color))
            throw new JsonException("Color must use opaque #RRGGBB syntax.");
        return color;
    }

    public override void Write(
        Utf8JsonWriter writer,
        Rgba32 value,
        JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}

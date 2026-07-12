using System.IO;
using System.Text.Json;

namespace DesktopBuddy.Domain.Telemetry;

public static class TelemetrySerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static void WriteFrame(Stream stream, TelemetryFrame frame)
    {
        JsonSerializer.Serialize(stream, frame, Options);
        stream.WriteByte((byte)'\n');
    }

    public static void WriteEnvelope(Stream stream, TelemetryEnvelope envelope) =>
        JsonSerializer.Serialize(stream, envelope, Options);

    public static TelemetryEnvelope ReadEnvelope(Stream stream) =>
        JsonSerializer.Deserialize<TelemetryEnvelope>(stream, Options);
}

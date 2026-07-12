using System.Collections.Generic;
using System.Text.Json;

namespace DesktopBuddy.Domain.Automation;

public sealed record InputTrace(string Format, ulong Seed, IReadOnlyList<InputTraceEvent> Events);
public sealed record InputTraceEvent(long Tick, string Kind, string Target, float X, float Y, int Button = 0, int Key = 0);

public static class TracePromoter
{
    public static string Promote(InputTrace trace, string id = "TODO_trace_journey")
    {
        var steps = new List<Dictionary<string, object?>>();
        foreach (InputTraceEvent sample in trace.Events)
        {
            string? step = sample.Kind switch
            {
                "pointer_press" => "pointer_press",
                "pointer_motion" => "drag",
                "pointer_release" => "pointer_release",
                "key_press" => "press_key",
                _ => null,
            };
            if (step is null) continue;
            steps.Add(new Dictionary<string, object?>
            {
                ["step"] = step, ["target"] = sample.Target,
                ["x"] = sample.Target == "sandbox" ? sample.X : null,
                ["y"] = sample.Target == "sandbox" ? sample.Y : null,
                ["key"] = sample.Key == 0 ? null : sample.Key,
            });
        }
        var draft = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["description"] = "TODO: harden promoted trace and replace residual coordinates",
            ["milestone"] = 1,
            ["setup"] = new Dictionary<string, object?> { ["scene"] = "buddy_lab", ["seed"] = trace.Seed, ["save"] = "fresh" },
            ["steps"] = steps,
            ["assertions"] = new[] { new Dictionary<string, object?> { ["predicate"] = "TODO_add_semantic_assertion", ["equals"] = true } },
        };
        return JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true });
    }
}

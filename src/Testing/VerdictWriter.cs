using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Emits the machine-readable verdict every runner produces. The verdict is
/// always printed to stdout on a single <c>[VERDICT]</c> line (so CI without an
/// artifacts directory still captures it) and, when an artifacts directory is
/// provided, written to <c>&lt;kind&gt;_&lt;id&gt;.verdict.json</c> there
/// (AGENT_VERIFICATION_AND_E2E.md Section 2). Artifacts are diagnostic output,
/// not save data, so plain .NET file APIs are used.
/// </summary>
public static class VerdictWriter
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(
        string kind,
        string id,
        ulong seed,
        bool passed,
        IReadOnlyList<StartupCheck> checks,
        IReadOnlyList<string> messages,
        long durationMs,
        string? artifactsDir)
    {
        var verdict = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["id"] = id,
            ["seed"] = seed,
            ["passed"] = passed,
            ["durationMs"] = durationMs,
            ["checks"] = checks.Select(c => new Dictionary<string, object?>
            {
                ["name"] = c.Name,
                ["passed"] = c.Passed,
                ["detail"] = c.Detail,
            }).ToList(),
            ["messages"] = messages,
        };

        string json = JsonSerializer.Serialize(verdict, Options);

        // Compact single line for log scraping, then the indented body for humans.
        GD.Print($"[VERDICT] {{\"kind\":\"{kind}\",\"id\":\"{id}\",\"passed\":{passed.ToString().ToLowerInvariant()}}}");
        GD.Print(json);

        if (!string.IsNullOrEmpty(artifactsDir))
        {
            Directory.CreateDirectory(artifactsDir);
            string path = Path.Combine(artifactsDir, $"{kind}_{id}.verdict.json");
            File.WriteAllText(path, json);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesktopBuddy.Domain.Automation;

/// <summary>
/// Which entrypoint the process was launched into. Selected from the user
/// command-line arguments that follow <c>--</c> on the Godot command line.
/// </summary>
public enum RunnerMode
{
    /// <summary>Ordinary interactive boot into the sandbox.</summary>
    Normal,

    /// <summary>Headless seeded physics/scene scenario runner.</summary>
    Scenario,

    /// <summary>End-to-end journey runner driving the real input path.</summary>
    Journey,
}

/// <summary>
/// Parsed form of the headless runner / automation command-line contract
/// (ROADMAP.md Milestone 0, AGENT_VERIFICATION_AND_E2E.md Sections 2-3):
/// <code>
///   godot --headless -- --scenario=&lt;id&gt; --seed=&lt;n&gt;
///   godot --headless -- --journey=&lt;id&gt;  --seed=&lt;n&gt; --artifacts=&lt;dir&gt;
///   godot            -- --automation
/// </code>
/// This is pure domain logic with no Godot dependency so the argument contract
/// is covered by fast <c>dotnet test</c> unit tests. The Godot side passes
/// <see cref="Godot.OS.GetCmdlineUserArgs"/> straight into <see cref="Parse"/>.
/// </summary>
public sealed record RunnerArguments
{
    /// <summary>Entrypoint selected by the arguments.</summary>
    public RunnerMode Mode { get; init; } = RunnerMode.Normal;

    /// <summary>Scenario id when <see cref="Mode"/> is <see cref="RunnerMode.Scenario"/>.</summary>
    public string? ScenarioId { get; init; }

    /// <summary>Journey id when <see cref="Mode"/> is <see cref="RunnerMode.Journey"/>.</summary>
    public string? JourneyId { get; init; }

    /// <summary>
    /// Explicit seed for the injected RNG service. <see langword="null"/> means
    /// "seed from entropy" (production); every automated run supplies one so the
    /// behavior/decision stream is repeatable.
    /// </summary>
    public ulong? Seed { get; init; }

    /// <summary>Directory for machine-readable verdicts, telemetry, and traces.</summary>
    public string? ArtifactsDir { get; init; }

    /// <summary>
    /// True when <c>--automation</c> was passed explicitly. The automation
    /// surface is also composed implicitly for scenario/journey runs; see
    /// <see cref="AutomationEnabled"/>.
    /// </summary>
    public bool AutomationRequested { get; init; }

    /// <summary>
    /// Whether the development-only <c>AutomationDriver</c> should be composed.
    /// True for any non-normal runner or an explicit <c>--automation</c> flag.
    /// The caller still gates this behind a debug-build check; release exports
    /// contain no automation code paths at all.
    /// </summary>
    public bool AutomationEnabled => AutomationRequested || Mode != RunnerMode.Normal;

    /// <summary>
    /// Parse the user argument list (the tokens after <c>--</c>). Unknown tokens
    /// are ignored so future flags and engine passthrough do not break older
    /// runners. Throws <see cref="ArgumentException"/> only on genuinely
    /// contradictory or malformed input, which is an actionable failure.
    /// </summary>
    public static RunnerArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? scenarioId = null;
        string? journeyId = null;
        ulong? seed = null;
        string? artifactsDir = null;
        bool automationRequested = false;

        foreach (string raw in args)
        {
            if (string.IsNullOrEmpty(raw) || !raw.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string body = raw[2..];
            string key;
            string? value;
            int eq = body.IndexOf('=');
            if (eq >= 0)
            {
                key = body[..eq];
                value = body[(eq + 1)..];
            }
            else
            {
                key = body;
                value = null;
            }

            switch (key)
            {
                case "scenario":
                    scenarioId = RequireValue(key, value);
                    break;
                case "journey":
                    journeyId = RequireValue(key, value);
                    break;
                case "artifacts":
                    artifactsDir = RequireValue(key, value);
                    break;
                case "seed":
                    seed = ParseSeed(RequireValue(key, value));
                    break;
                case "automation":
                    automationRequested = true;
                    break;
                default:
                    // Unknown flag: ignore (engine passthrough / forward compatibility).
                    break;
            }
        }

        if (scenarioId is not null && journeyId is not null)
        {
            throw new ArgumentException(
                "--scenario and --journey are mutually exclusive.", nameof(args));
        }

        RunnerMode mode =
            scenarioId is not null ? RunnerMode.Scenario :
            journeyId is not null ? RunnerMode.Journey :
            RunnerMode.Normal;

        return new RunnerArguments
        {
            Mode = mode,
            ScenarioId = scenarioId,
            JourneyId = journeyId,
            Seed = seed,
            ArtifactsDir = artifactsDir,
            AutomationRequested = automationRequested,
        };
    }

    private static string RequireValue(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"--{key} requires a non-empty value.", nameof(value));
        }

        return value;
    }

    private static ulong ParseSeed(string value)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            throw new ArgumentException(
                $"--seed must be a non-negative 64-bit integer, got '{value}'.", nameof(value));
        }

        return seed;
    }
}

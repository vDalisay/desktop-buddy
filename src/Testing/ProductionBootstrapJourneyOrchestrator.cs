using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Environment;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Scenes;
using DesktopBuddy.Domain.Work;
using DesktopBuddy.Persistence;
using Godot;
using FileAccess = Godot.FileAccess;

namespace DesktopBuddy.Testing;

/// <summary>
/// Process-level journey driver for the master-release persistence gate. The parent only prepares
/// isolated save fixtures and launches the same executable again in normal mode. Every child then
/// enters <see cref="Bootstrap"/>'s production sandbox/persistence composition and is observed only
/// after that composition completes by <see cref="ProductionBootstrapJourneyProbe"/>.
/// </summary>
public partial class ProductionBootstrapJourneyOrchestrator : Node
{
    public const string JourneyId = "production_bootstrap_persistence";
    private const double FixtureCashPerPain = 0.01;
    private const int ChildTimeoutSeconds = 90;
    private static readonly BuddyIdentityId SecondFixtureBuddyId = BuddyIdentityId.From(
        Guid.Parse("781d1668-ef7f-4ea5-bd0a-a0aa20260909"));
    private static readonly Guid InactiveLegacyCharacterId =
        Guid.Parse("9b36ef94-67e3-4cb9-a75f-202609090001");

    private RunnerArguments _args = new();

    public static bool Handles(RunnerArguments args) =>
        args.Mode == RunnerMode.Journey &&
        string.Equals(args.JourneyId, JourneyId, StringComparison.Ordinal);

    public void Configure(RunnerArguments args) => _args = args;

    public override async void _Ready()
    {
        ulong seed = _args.Seed ?? 0;
        var stopwatch = Stopwatch.StartNew();
        var checks = new List<StartupCheck>();
        bool passed = true;

        try
        {
            string journeyPath = $"res://tests/journeys/{JourneyId}.json";
            if (!FileAccess.FileExists(journeyPath))
            {
                checks.Add(new StartupCheck("journey_file_exists", false, journeyPath));
                passed = false;
            }
            else
            {
                string text;
                using (FileAccess file = FileAccess.Open(journeyPath, FileAccess.ModeFlags.Read))
                    text = file.GetAsText();
                using JsonDocument doc = JsonDocument.Parse(text);
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("phases", out JsonElement phases) ||
                    phases.ValueKind != JsonValueKind.Array)
                {
                    checks.Add(new StartupCheck("journey_has_phases", false, "missing phases array"));
                    passed = false;
                }
                else
                {
                    passed = await RunMatchingPhasesAsync(phases, seed, checks);
                }
            }
        }
        catch (Exception exception)
        {
            checks.Add(new StartupCheck("orchestrator_exception", false, exception.ToString()));
            passed = false;
        }

        stopwatch.Stop();
        VerdictWriter.Write(
            "journey",
            JourneyId,
            seed,
            passed,
            checks,
            new[] { $"seed={seed}", $"scope={CurrentScopeName()}" },
            stopwatch.ElapsedMilliseconds,
            _args.ArtifactsDir);
        Log.Info("BootstrapJourney", $"Tagged production Bootstrap journey {(passed ? "PASSED" : "FAILED")}.");
        GodotInteropShutdown.PrepareForQuit();
        GetTree().Quit(passed ? 0 : 1);
    }

    private async Task<bool> RunMatchingPhasesAsync(
        JsonElement phases,
        ulong seed,
        List<StartupCheck> checks)
    {
        string currentScope = CurrentScopeName();
        if (currentScope == "unsupported")
        {
            checks.Add(new StartupCheck(
                "tagged_build_scope",
                false,
                "Journey must run from an exported Initial Steam Demo or Next Fest debug build."));
            return false;
        }

        string artifacts = Path.GetFullPath(
            _args.ArtifactsDir ?? Path.Combine(ProjectSettings.GlobalizePath("res://"), ".artifacts", JourneyId));
        string fixtureRoot = Path.Combine(artifacts, "save-fixture");
        Directory.CreateDirectory(artifacts);

        bool passed = true;
        int matched = 0;
        for (int index = 0; index < phases.GetArrayLength(); index++)
        {
            JsonElement phase = phases[index];
            string requiredScope = phase.TryGetProperty("build_scope", out JsonElement scopeElement)
                ? scopeElement.GetString() ?? string.Empty
                : string.Empty;
            if (!string.Equals(requiredScope, currentScope, StringComparison.Ordinal))
                continue;

            matched++;
            string phaseName = phase.TryGetProperty("id", out JsonElement idElement)
                ? idElement.GetString() ?? $"phase_{index + 1}"
                : $"phase_{index + 1}";
            JsonElement setup = phase.TryGetProperty("setup", out JsonElement setupElement)
                ? setupElement
                : default;
            string precondition = setup.ValueKind == JsonValueKind.Object &&
                setup.TryGetProperty("precondition", out JsonElement preconditionElement)
                    ? preconditionElement.GetString() ?? "preserve"
                    : "preserve";
            int expectedExit = phase.TryGetProperty("expected_exit", out JsonElement exitElement) &&
                exitElement.TryGetInt32(out int configuredExit)
                    ? configuredExit
                    : 0;
            bool assertSemanticUnchanged = phase.TryGetProperty(
                "assert_semantic_unchanged", out JsonElement unchangedElement) &&
                unchangedElement.ValueKind == JsonValueKind.True;

            await PrepareFixtureAsync(fixtureRoot, precondition);
            string? beforeFingerprint = assertSemanticUnchanged ? FingerprintSemanticFiles(fixtureRoot) : null;
            int exitCode = await RunProductionChildAsync(
                index,
                phaseName,
                fixtureRoot,
                artifacts,
                seed);

            bool exitMatches = exitCode == expectedExit;
            checks.Add(new StartupCheck(
                $"phase:{phaseName}:exit",
                exitMatches,
                $"expected={expectedExit} actual={exitCode}"));
            passed &= exitMatches;

            if (assertSemanticUnchanged)
            {
                string afterFingerprint = FingerprintSemanticFiles(fixtureRoot);
                bool unchanged = string.Equals(beforeFingerprint, afterFingerprint, StringComparison.Ordinal);
                checks.Add(new StartupCheck(
                    $"phase:{phaseName}:semantic_unchanged",
                    unchanged,
                    unchanged ? "semantic fixture unchanged" : "semantic files changed during refusal"));
                passed &= unchanged;
            }

            if (!exitMatches)
                break;
        }

        bool matchedAny = matched > 0;
        checks.Add(new StartupCheck(
            "matching_scope_phases",
            matchedAny,
            $"scope={currentScope} matched={matched}"));
        return passed && matchedAny;
    }

    private async Task PrepareFixtureAsync(string root, string precondition)
    {
        switch (precondition)
        {
            case "legacy":
                ResetDirectory(root);
                await WriteLegacyFixtureAsync(root);
                break;

            case "preserve":
                Directory.CreateDirectory(root);
                break;

            case "incomplete_promotion":
            {
                string canonical = Path.Combine(root, SteamCloudSavePolicy.ProgressFileName);
                string staged = canonical + ".next";
                if (!File.Exists(canonical))
                    throw new FileNotFoundException("Committed fixture has no canonical account progress to stage.", canonical);
                File.Move(canonical, staged, overwrite: true);
                break;
            }

            case "malformed_manifest":
            {
                string manifest = Path.Combine(root, SceneProgressTransactionStore.ManifestFileName);
                if (!File.Exists(manifest))
                    throw new FileNotFoundException("Committed fixture has no Scene manifest to corrupt.", manifest);
                File.WriteAllText(manifest, "{ definitely-not-json", new UTF8Encoding(false));
                break;
            }

            case "scene_upgraded":
                ResetDirectory(root);
                await WriteSceneUpgradedFixtureAsync(root);
                break;

            default:
                throw new InvalidDataException($"Unknown production-bootstrap fixture precondition '{precondition}'.");
        }
    }

    private static async Task WriteLegacyFixtureAsync(string root)
    {
        Directory.CreateDirectory(root);
        var store = new JsonProgressStore(
            Path.Combine(root, SteamCloudSavePolicy.ProgressFileName),
            Path.Combine(root, SteamCloudSavePolicy.SettingsFileName),
            new AtomicSaveFileSystem());
        await store.SaveProgressAsync(LegacyFixture(), CancellationToken.None);
    }

    private static async Task WriteSceneUpgradedFixtureAsync(string root)
    {
        await WriteLegacyFixtureAsync(root);
        string progressPath = Path.Combine(root, SteamCloudSavePolicy.ProgressFileName);
        var files = new AtomicSaveFileSystem();
        BuildScopePolicy nextFest = BuildScopePolicy.Resolve(
            itchIo: false,
            steamDemo: true,
            nextFestDemo: true,
            fullRelease: false);
        var bootstrap = new SceneProgressBootstrapCoordinator(
            nextFest,
            FixtureCashPerPain,
            new SceneProgressTransactionStore(root, files),
            new NextFestMigrationStore(progressPath, files));
        SceneProgressBootstrapResult boot = await bootstrap.LoadOrMigrateAsync(
            LegacyFixture(),
            new CanonicalRoomPosition(0.35f, 0.5f),
            token: CancellationToken.None);

        SceneProgressCoordinator coordinator = boot.Coordinator;
        if (!coordinator.TryGetBuddy(BuddyIdentityId.LegacyPrimary, out BuddyIdentityState? primary) || primary is null)
            throw new InvalidDataException("Migrated fixture did not contain its first Buddy identity.");

        // Give the migrated Buddy a Character different from the compatibility selection expected
        // for the roster head. If production ever goes back to binding CharacterSelectionRuntime by
        // LegacyPrimary instead of Scene order, the child boot fails before the probe can pass.
        primary.SetCharacter(InactiveLegacyCharacterId);
        BuddyIdentitySnapshot secondSnapshot = primary.Snapshot() with
        {
            BuddyIdentityId = SecondFixtureBuddyId,
            Revision = 0,
            CharacterId = null,
            Mood = -35.0f,
            Fullness = 25.0f,
            HarmfulContentIds = Array.Empty<string>(),
        };
        if (!coordinator.RegisterBuddyIdentity(new BuddyIdentityState(secondSnapshot)))
            throw new InvalidOperationException("Could not register second production-bootstrap fixture Buddy.");

        // Add the new identity, remove the migrated placement, then add the migrated identity back.
        // Scene document order is now [new Buddy, migrated Buddy], proving runtime identity does not
        // inherit the migration ID's historical first-actor status.
        SceneLibraryResult added = coordinator.AddBuddyToScene(
            coordinator.ActiveSceneId,
            SecondFixtureBuddyId,
            new CanonicalRoomPosition(0.70f, 0.5f));
        if (!added.Succeeded)
            throw new InvalidOperationException($"Could not add second fixture Buddy to active Scene: {added.Status}.");
        SceneLibraryResult removedPrimary = coordinator.RemoveBuddyFromScene(
            coordinator.ActiveSceneId,
            BuddyIdentityId.LegacyPrimary);
        if (!removedPrimary.Succeeded)
            throw new InvalidOperationException($"Could not reorder migrated fixture Buddy: {removedPrimary.Status}.");
        SceneLibraryResult readdedPrimary = coordinator.AddBuddyToScene(
            coordinator.ActiveSceneId,
            BuddyIdentityId.LegacyPrimary,
            new CanonicalRoomPosition(0.25f, 0.5f));
        if (!readdedPrimary.Succeeded)
            throw new InvalidOperationException($"Could not restore migrated fixture Buddy: {readdedPrimary.Status}.");

        // Keep a second persisted Scene inactive at boot. It contains only the migrated identity, so
        // switching to it proves the authored compatibility actor can change persistent identity,
        // while the two secondary-actor nodes from the outgoing Scene are really torn down.
        SceneLibraryResult targetCreated = coordinator.CreateScene("Switch Target");
        if (!targetCreated.Succeeded || targetCreated.Scene is null)
            throw new InvalidOperationException($"Could not create switch-target fixture Scene: {targetCreated.Status}.");
        SceneLibraryResult targetBuddy = coordinator.AddBuddyToScene(
            targetCreated.Scene.SceneId,
            BuddyIdentityId.LegacyPrimary,
            new CanonicalRoomPosition(0.55f, 0.5f));
        if (!targetBuddy.Succeeded)
            throw new InvalidOperationException($"Could not add target fixture Buddy: {targetBuddy.Status}.");

        await coordinator.FlushAsync(force: true, CancellationToken.None);
    }

    private static ProgressSave LegacyFixture() => new()
    {
        Revision = 9,
        BalanceMilliCredits = 123_000,
        UnlockedToolIds = [ContentIds.ToolGrab],
        SelectedToolId = ContentIds.ToolGrab,
        Mood = 15.0f,
        Fullness = 60.0f,
        Work = new WorkProgressSave
        {
            Revision = 4,
            KeyboardPresses = 77,
            MouseClicks = 5,
        },
        Environment = new EnvironmentProgressSave(),
    };

    private async Task<int> RunProductionChildAsync(
        int phaseIndex,
        string phaseName,
        string fixtureRoot,
        string artifacts,
        ulong seed)
    {
        string executable = OS.GetExecutablePath();
        string logPath = Path.Combine(artifacts, $"{JourneyId}_{phaseName}.log");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? System.Environment.CurrentDirectory,
        };

        if (DisplayServer.GetName() == "headless")
            start.ArgumentList.Add("--headless");
        start.ArgumentList.Add("--fixed-fps");
        start.ArgumentList.Add("120");
        start.ArgumentList.Add("--rendering-driver");
        start.ArgumentList.Add("opengl3");
        start.ArgumentList.Add("--log-file");
        start.ArgumentList.Add(logPath);
        if (OS.HasFeature("editor"))
        {
            start.ArgumentList.Add("--path");
            start.ArgumentList.Add(ProjectSettings.GlobalizePath("res://"));
        }
        start.ArgumentList.Add("--");
        start.ArgumentList.Add($"--bootstrap-journey={JourneyId}");
        start.ArgumentList.Add($"--bootstrap-phase={phaseIndex}");
        start.ArgumentList.Add($"--bootstrap-save-root={fixtureRoot}");
        start.ArgumentList.Add($"--seed={seed}");
        start.ArgumentList.Add($"--artifacts={artifacts}");

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start production Bootstrap child process.");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(ChildTimeoutSeconds));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return 124;
        }
        return process.ExitCode;
    }

    private static string CurrentScopeName()
    {
        if (DemoScope.IsSteamDemo && DemoScope.IsNextFestDemo && DemoScope.IncludesScenes)
            return "next_fest";
        if (DemoScope.IsSteamDemo && !DemoScope.IsNextFestDemo && !DemoScope.IncludesScenes)
            return "initial_demo";
        return "unsupported";
    }

    private static string FingerprintSemanticFiles(string root)
    {
        if (!Directory.Exists(root))
            return "<missing>";

        using var sha = SHA256.Create();
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .Where(path => !string.Equals(
                         Path.GetFileName(path),
                         SteamCloudSavePolicy.SettingsFileName,
                         StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            byte[] name = Encoding.UTF8.GetBytes(relative + "\n");
            sha.TransformBlock(name, 0, name.Length, null, 0);
            byte[] bytes = File.ReadAllBytes(path);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static void ResetDirectory(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
    }
}

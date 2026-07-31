using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Behavior;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Content;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Content;
using DesktopBuddy.Domain.Damage;
using DesktopBuddy.Domain.Economy;
using DesktopBuddy.Domain.Mood;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Domain.Platform;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Domain.Tools;
using DesktopBuddy.Economy;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Objects;
using DesktopBuddy.Interaction;
using DesktopBuddy.Persistence;
using DesktopBuddy.Platform;
using DesktopBuddy.Presentation3D;
using DesktopBuddy.Tools;
using Godot;
using FileAccess = Godot.FileAccess;
using DomainInputMode = DesktopBuddy.Domain.Platform.InputMode;

namespace DesktopBuddy.Testing;

/// <summary>
/// Runs an end-to-end journey from <c>tests/journeys/&lt;id&gt;.json</c> via the
/// <c>--journey=&lt;id&gt;</c> entrypoint (AGENT_VERIFICATION_AND_E2E.md Section 3).
/// Journeys assert on read-only state with an explicit timeout; there are no
/// fixed sleeps and no retries. Milestone 0 ships the boot smoke journey and a
/// initial predicate set plus the Milestone 1 buddy-lab spawn/settle journey;
/// the step vocabulary grows with the milestones that need it.
/// </summary>
public partial class JourneyRunner : Node
{
    private RunnerArguments _args = new();

    public void Configure(RunnerArguments args) => _args = args;

    public override async void _Ready()
    {
        string id = _args.JourneyId ?? string.Empty;
        string path = $"res://tests/journeys/{id}.json";
        var stopwatch = Stopwatch.StartNew();

        if (!FileAccess.FileExists(path))
        {
            Fail(id, _args.Seed ?? 0, stopwatch, "journey_file_exists", path, 3);
            return;
        }

        string text;
        using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
        {
            text = file.GetAsText();
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException e)
        {
            Fail(id, _args.Seed ?? 0, stopwatch, "journey_json_valid", e.Message, 3);
            return;
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            ulong seed = ResolveSeed(root);
            Log.Info("Journey", $"Running journey '{id}' seed={seed}.");

            if (root.TryGetProperty("phases", out JsonElement phases) &&
                phases.ValueKind == JsonValueKind.Array &&
                _args.JourneyPhase is null)
            {
                await RunPhaseProcessesAsync(id, seed, phases, stopwatch);
                return;
            }

            JsonElement executionRoot = root;
            string verdictId = id;
            if (_args.JourneyPhase is int phaseIndex)
            {
                if (!root.TryGetProperty("phases", out phases) ||
                    phases.ValueKind != JsonValueKind.Array ||
                    phaseIndex < 0 ||
                    phaseIndex >= phases.GetArrayLength())
                {
                    Fail(id, seed, stopwatch, "journey_phase_exists", phaseIndex.ToString(), 3);
                    return;
                }
                executionRoot = phases[phaseIndex];
                verdictId = $"{id}_phase_{phaseIndex + 1}";
            }

            Dictionary<string, bool> state = await ComputeStateAsync(executionRoot, seed);

            var checks = new List<StartupCheck>();
            bool passed = true;
            foreach (string interpreterCheck in new[] { "step_known", "anchor_known" })
            {
                if (state.TryGetValue(interpreterCheck, out bool interpreterOk) && !interpreterOk)
                {
                    checks.Add(new StartupCheck(interpreterCheck, false,
                        state.TryGetValue($"{interpreterCheck}_detail", out _) ? "see journey log" : "step interpreter failure"));
                    passed = false;
                }
            }

            if (executionRoot.TryGetProperty("assertions", out JsonElement assertions) &&
                assertions.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement assertion in assertions.EnumerateArray())
                {
                    if (!assertion.TryGetProperty("predicate", out JsonElement predicateElement))
                        continue;
                    string predicate = predicateElement.GetString() ?? "";
                    bool expected = !assertion.TryGetProperty("equals", out JsonElement eq) || eq.GetBoolean();

                    if (!state.TryGetValue(predicate, out bool actual))
                    {
                        checks.Add(new StartupCheck($"assert:{predicate}", false, "unknown predicate"));
                        passed = false;
                        continue;
                    }

                    bool ok = actual == expected;
                    checks.Add(new StartupCheck($"assert:{predicate}", ok, $"expected={expected} actual={actual}"));
                    passed &= ok;
                }
            }
            else
            {
                checks.Add(new StartupCheck("journey_has_assertions", false, "no assertions array"));
                passed = false;
            }

            stopwatch.Stop();
            VerdictWriter.Write("journey", verdictId, seed, passed, checks, new[] { $"seed={seed}" },
                stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
            Log.Info("Journey", $"Journey '{verdictId}' {(passed ? "PASSED" : "FAILED")}.");
            QuitSafely(passed ? 0 : 1);
        }
    }

    private async System.Threading.Tasks.Task RunPhaseProcessesAsync(
        string id,
        ulong seed,
        JsonElement phases,
        Stopwatch stopwatch)
    {
        string projectRoot = ProjectSettings.GlobalizePath("res://");
        string artifacts = Path.GetFullPath(
            _args.ArtifactsDir ?? Path.Combine(projectRoot, ".artifacts", "journeys", id));
        string fixtureDirectory = Path.Combine(artifacts, "fixture");
        Directory.CreateDirectory(fixtureDirectory);
        string fixture = _args.SaveFixture ??
            Path.Combine(fixtureDirectory, "progress.json");
        var checks = new List<StartupCheck>();
        bool passed = true;

        for (int index = 0; index < phases.GetArrayLength(); index++)
        {
            string phaseName = phases[index].TryGetProperty("id", out JsonElement name)
                ? name.GetString() ?? $"phase_{index + 1}"
                : $"phase_{index + 1}";
            string logPath = Path.Combine(artifacts, $"{id}_{phaseName}.log");
            var start = new ProcessStartInfo(OS.GetExecutablePath())
            {
                UseShellExecute = false,
                WorkingDirectory = projectRoot,
            };
            if (DisplayServer.GetName() == "headless")
                start.ArgumentList.Add("--headless");
            start.ArgumentList.Add("--fixed-fps");
            start.ArgumentList.Add("120");
            start.ArgumentList.Add("--path");
            start.ArgumentList.Add(projectRoot);
            start.ArgumentList.Add("--rendering-driver");
            start.ArgumentList.Add("opengl3");
            start.ArgumentList.Add("--log-file");
            start.ArgumentList.Add(logPath);
            start.ArgumentList.Add("--");
            start.ArgumentList.Add($"--journey={id}");
            start.ArgumentList.Add($"--journey-phase={index}");
            start.ArgumentList.Add($"--seed={seed}");
            start.ArgumentList.Add($"--artifacts={artifacts}");
            start.ArgumentList.Add($"--save-fixture={fixture}");
            if (_args.Presentation is RunnerPresentation presentation)
            {
                start.ArgumentList.Add(
                    presentation == RunnerPresentation.Legacy
                        ? "--presentation=legacy"
                        : "--presentation=mii3d");
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                checks.Add(new StartupCheck($"phase:{phaseName}", false, "process failed to start"));
                passed = false;
                break;
            }

            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                checks.Add(new StartupCheck($"phase:{phaseName}", false, "90 second hard timeout"));
                passed = false;
                break;
            }

            bool phasePassed = process.ExitCode == 0;
            checks.Add(new StartupCheck(
                $"phase:{phaseName}",
                phasePassed,
                $"exit={process.ExitCode} log={logPath}"));
            passed &= phasePassed;
            if (!phasePassed)
                break;
        }

        stopwatch.Stop();
        VerdictWriter.Write(
            "journey",
            id,
            seed,
            passed,
            checks,
            new[] { $"seed={seed}", $"fixture={fixture}" },
            stopwatch.ElapsedMilliseconds,
            _args.ArtifactsDir);
        Log.Info("Journey", $"Phased journey '{id}' {(passed ? "PASSED" : "FAILED")}.");
        QuitSafely(passed ? 0 : 1);
    }

    private ulong ResolveSeed(JsonElement root)
    {
        if (_args.Seed is ulong fromArgs)
        {
            return fromArgs;
        }

        if (root.TryGetProperty("setup", out JsonElement setup) &&
            setup.TryGetProperty("seed", out JsonElement seed) &&
            seed.TryGetUInt64(out ulong value))
        {
            return value;
        }

        return 0;
    }

    private async System.Threading.Tasks.Task<Dictionary<string, bool>> ComputeStateAsync(
        JsonElement root,
        ulong seed)
    {
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);

        // Yield one frame so we leave the initial _Ready setup cascade before
        // adding the sandbox to the tree root.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        StartupReport report = StartupValidator.Validate();
        state["startup_ok"] = report.Ok;

        string scene = "sandbox";
        int timeoutPhysicsTicks = 720;
        if (root.TryGetProperty("setup", out JsonElement setup))
        {
            if (setup.TryGetProperty("scene", out JsonElement sceneElement))
            {
                scene = sceneElement.GetString() ?? scene;
            }

            if (setup.TryGetProperty("timeout_physics_ticks", out JsonElement timeoutElement) &&
                timeoutElement.TryGetInt32(out int configuredTimeout) &&
                configuredTimeout > 0)
            {
                timeoutPhysicsTicks = configuredTimeout;
            }
        }

        string? exercise = setup.ValueKind == JsonValueKind.Object &&
            setup.TryGetProperty("exercise", out JsonElement exerciseElement)
                ? exerciseElement.GetString()
                : null;
        if (exercise is "care_persistence_write" or "care_persistence_resume")
        {
            await ComputeCarePersistenceStateAsync(state, seed, exercise);
            return state;
        }

        if (string.Equals(scene, "buddy_lab", StringComparison.Ordinal))
        {
            await ComputeBuddyLabStateAsync(state, seed, timeoutPhysicsTicks, root);
            return state;
        }

        if (root.TryGetProperty("setup", out JsonElement sandboxSetup) &&
            sandboxSetup.TryGetProperty("exercise", out JsonElement sandboxExercise) &&
            sandboxExercise.GetString() == "shell_modes")
        {
            await ComputeShellModeStateAsync(state);
            return state;
        }

        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        bool composed = false;
        if (packed is not null)
        {
            Node instance = packed.Instantiate();
            GetTree().Root.AddChild(instance);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            composed = instance is SandboxRoot && instance.IsInsideTree();
            instance.QueueFree();
        }

        state["sandbox_composed"] = composed;
        return state;
    }

    private async System.Threading.Tasks.Task ComputeCarePersistenceStateAsync(
        Dictionary<string, bool> state,
        ulong seed,
        string exercise)
    {
        if (string.IsNullOrWhiteSpace(_args.SaveFixture))
        {
            state["fixture_configured"] = false;
            return;
        }

        string fixture = Path.GetFullPath(_args.SaveFixture);
        string settingsFixture = fixture + ".settings";
        var store = new JsonProgressStore(fixture, settingsFixture);
        var packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null || packed.Instantiate() is not BuddyLab lab)
        {
            state["lab_composed"] = false;
            return;
        }

        double cashPerPain = lab.Pipeline.RequirePainProfile().CashPerPain;
        BuddyTraits expectedTraits = BuddyTraits.Sample(
            new SeededRandomSource(seed ^ 0xA18E_5EED_D15C_A11FUL));
        BuddyProgressState progress;
        SaveLoadStatus loadStatus;
        long savedRevision;
        float? loadedMood = null;
        if (exercise == "care_persistence_write")
        {
            progress = new BuddyProgressState(cashPerPain, traits: expectedTraits);
            loadStatus = SaveLoadStatus.NewSave;
            savedRevision = -1;
        }
        else
        {
            LoadResult<ProgressSave> loaded =
                await store.LoadProgressAsync(CancellationToken.None);
            state["progress_loaded"] = loaded.Status is
                SaveLoadStatus.Loaded or SaveLoadStatus.BackupRecovered;
            if (loaded.Value is null)
                return;
            progress = ProgressSavePolicy.CreateState(loaded.Value, cashPerPain);
            loadStatus = loaded.Status;
            savedRevision = progress.Revision;
            loadedMood = loaded.Value.Mood;
        }

        var economy = new EconomyService(progress, CatalogueLoader.Catalogue);
        var saves = new SaveCoordinator(progress, store, savedRevision);
        lab.Configure(new RunContext(
            progress,
            economy,
            store,
            saves,
            new LocalSettingsSave(),
            loadStatus));
        GetTree().Root.AddChild(lab);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        state["lab_composed"] = lab.IsInsideTree() && lab.Buddy.IsInitialized;
        state["fixture_configured"] = true;

        if (exercise == "care_persistence_write")
        {
            long careBefore = progress.Statistics.CareAwards;
            await PressKeyAsync(Key.E);
            for (int tick = 0;
                 tick < 720 && progress.Statistics.CareAwards == careBefore;
                 tick++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }

            long balanceBefore = progress.BalanceMilliCredits;
            await ExerciseM3GloveStrikeAsync(state, lab);
            await PressKeyAsync(Key.T);

            // Exercise a deliberately transient state through the laboratory's real key
            // route. Consciousness must reset on the second process and never enter JSON.
            if (lab.Buddy.CurrentConsciousness ==
                DesktopBuddy.Domain.Buddy.Consciousness.Unconscious)
            {
                await PressKeyAsync(Key.U);
            }
            await PressKeyAsync(Key.U);

            // Match the production close ordering: stop gameplay mutation, settle/stop
            // lifecycle mutation, then take the forced final snapshot.
            lab.SetPhysicsProcess(false);
            lab.Lifecycle.BeginShutdown();
            await saves.FlushProgressAsync(force: true);
            state["real_input_exercised"] =
                lab.Controls.LastControlKey == Key.U &&
                lab.Buddy.CurrentConsciousness ==
                    DesktopBuddy.Domain.Buddy.Consciousness.Unconscious;
            state["care_consumed"] =
                progress.Statistics.CareAwards == careBefore + 1 &&
                progress.InterestIn(FunActivityId.Treat) <
                    FunInterestModel.MaximumInterest;
            state["damage_earned"] =
                progress.BalanceMilliCredits > balanceBefore &&
                progress.Statistics.ScoredImpacts > 0;
            state["harm_memory_recorded"] =
                progress.IsContentHarmful(ContentIds.ToolBoxingGlove);
            state["selection_changed"] = progress.SelectedTool == ToolId.Tickle;
            state["trait_sampled"] = progress.Traits == expectedTraits;
            state["fixture_saved"] = System.IO.File.Exists(fixture) && !saves.IsDirty;
        }
        else
        {
            // Inspect the session-resume checkpoint immediately after composition. Waiting
            // for "standing" would let the loaded mood/autonomy legitimately begin moving
            // and turn a safe-pose assertion into an ambient-behavior race.
            bool safeBodies = lab.Buddy.Rig.AllBodiesFinite() &&
                lab.Buddy.Recovery.AllBodiesInsideSafeBounds();
            foreach (PuppetPartBody body in lab.Buddy.Rig.Parts)
            {
                safeBodies &= body.LinearVelocity.Length() < 100.0f;
                safeBodies &= Math.Abs(body.AngularVelocity) < 2.0f;
            }

            state["balance_restored"] = progress.BalanceMilliCredits > 0;
            state["mood_restored"] =
                loadedMood.HasValue &&
                Math.Abs(progress.Mood - loadedMood.Value) < 0.1f &&
                progress.Statistics.CareAwards > 0;
            state["memory_restored"] =
                progress.IsContentHarmful(ContentIds.ToolBoxingGlove);
            state["selection_restored"] = progress.SelectedTool == ToolId.Tickle;
            state["trait_restored"] = progress.Traits == expectedTraits;
            state["safe_standing_resume"] = safeBodies &&
                lab.Buddy.Recovery.HardRecoveryCount == 0;
            state["transient_state_absent"] =
                !lab.Grab.IsGrabbing &&
                lab.Objects.Count == 0 &&
                !lab.Buddy.ObjectInteraction.IsHolding &&
                lab.Buddy.CurrentConsciousness ==
                    DesktopBuddy.Domain.Buddy.Consciousness.Conscious &&
                lab.Buddy.ObjectInteraction.CooldownTicksRemaining(
                    ContentIds.CareLabFood) == 0;
        }

        lab.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        async System.Threading.Tasks.Task PressKeyAsync(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    /// <summary>
    /// Milestone 2 mode-transition journey: drive the desktop shell through the
    /// input paths in-process synthesis can reach (the mode hotkey action, Escape,
    /// and clicks inside/outside the sandbox box) and assert Work/Play transitions
    /// and control recovery. Native passthrough/tray/resize are the owner-manual
    /// §5 matrix (AGENT_VERIFICATION_AND_E2E.md §6), not this journey.
    /// </summary>
    private async System.Threading.Tasks.Task ComputeShellModeStateAsync(Dictionary<string, bool> state)
    {
        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        if (packed is null || packed.Instantiate() is not SandboxRoot sandbox)
        {
            state["shell_composed"] = false;
            return;
        }

        GetTree().Root.AddChild(sandbox);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        DesktopShellController shell = sandbox.Shell;
        state["shell_composed"] = shell is not null && sandbox.IsInsideTree();
        state["shell_transparency_active"] = sandbox.Window.TransparencyActive;
        state["starts_in_work"] = shell!.Mode == DomainInputMode.Work;

        // Work Mode exposes only the six moving buddy bodies. Transparent client
        // space is deliberately absent so the native adapter can pass it through.
        IReadOnlyList<Rect2I> regions = shell.LastWorkModeHitRegions;
        bool tracksBuddy = regions.Count == PuppetRigProfile.RequiredPartCount;
        for (int index = 0; index < regions.Count && index < sandbox.Buddy.Rig.Parts.Count; index++)
        {
            PuppetPartBody part = sandbox.Buddy.Rig.Parts[index];
            PixelRect projected = SandboxProjection.SandboxRectToClient(
                part.GlobalPosition.X,
                part.GlobalPosition.Y,
                0.0,
                0.0,
                shell.EffectiveZoom);
            var clientPoint = new Vector2I(
                projected.X,
                projected.Y);
            bool contains = regions[index].HasPoint(clientPoint);
            tracksBuddy &= contains;
        }
        state["hit_regions_track_buddy"] = tracksBuddy;

        await ToggleAsync();
        state["toggle_enters_play"] = shell.Mode == DomainInputMode.Play;

        await EscapeAsync();
        state["escape_returns_to_work"] = shell.Mode == DomainInputMode.Work;

        Vector2 torsoStart = sandbox.Buddy.Rig.Torso.GlobalPosition;
        await PressWorldAsync(torsoStart);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        state["click_inside_enters_play"] = shell.Mode == DomainInputMode.Play;

        Rect2 box = sandbox.Boundaries.InnerBounds;
        Vector2 dragTarget = new(
            Mathf.Clamp(torsoStart.X + 80.0f, box.Position.X + 24.0f, box.End.X - 24.0f),
            Mathf.Clamp(torsoStart.Y - 40.0f, box.Position.Y + 24.0f, box.End.Y - 24.0f));
        await MoveWorldAsync(dragTarget, dragTarget - torsoStart);
        for (int tick = 0; tick < 30; tick++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        state["grab_cursor_follows_drag"] =
            sandbox.Grab.CurrentGrab.Active &&
            sandbox.Grab.CurrentGrab.CursorAnchor.DistanceTo(dragTarget) < 1.0f;
        state["buddy_follows_drag"] =
            sandbox.Buddy.Rig.Torso.GlobalPosition.DistanceTo(torsoStart) > 4.0f;
        await ReleaseWorldAsync(dragTarget);

        await EscapeAsync();
        Vector2 transparentPoint = box.Position + new Vector2(12.0f, 12.0f);
        Vector2I transparentClient = new(
            Mathf.RoundToInt(transparentPoint.X * (float)shell.EffectiveZoom),
            Mathf.RoundToInt(transparentPoint.Y * (float)shell.EffectiveZoom));
        bool transparentCovered = false;
        regions = shell.LastWorkModeHitRegions;
        for (int index = 0; index < regions.Count; index++)
            transparentCovered |= regions[index].HasPoint(transparentClient);
        state["transparent_space_is_passthrough_region"] = !transparentCovered;

        await ToggleAsync();
        await ClickWorldAsync(transparentPoint);
        state["click_outside_returns_to_work"] = shell.Mode == DomainInputMode.Work;

        state["ends_in_work"] = shell.Mode == DomainInputMode.Work;
        sandbox.QueueFree();
    }

    private async System.Threading.Tasks.Task ToggleAsync()
    {
        Input.ParseInputEvent(new InputEventAction { Action = InputActions.ToggleInputMode, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventAction { Action = InputActions.ToggleInputMode, Pressed = false });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async System.Threading.Tasks.Task EscapeAsync()
    {
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.Escape, Pressed = false });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async System.Threading.Tasks.Task ClickWorldAsync(Vector2 world)
    {
        await PressWorldAsync(world);
        await ReleaseWorldAsync(world);
    }

    private async System.Threading.Tasks.Task PressWorldAsync(Vector2 world)
    {
        Vector2 viewport = GetViewport().GetCanvasTransform() * world;
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left,
            Pressed = true,
            Position = viewport,
            GlobalPosition = viewport,
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async System.Threading.Tasks.Task MoveWorldAsync(Vector2 world, Vector2 relative)
    {
        Vector2 viewport = GetViewport().GetCanvasTransform() * world;
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            ButtonMask = MouseButtonMask.Left,
            Position = viewport,
            GlobalPosition = viewport,
            Relative = relative,
            Velocity = relative * 120.0f,
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async System.Threading.Tasks.Task ReleaseWorldAsync(Vector2 world)
    {
        Vector2 viewport = GetViewport().GetCanvasTransform() * world;
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            ButtonMask = 0,
            Pressed = false,
            Position = viewport,
            GlobalPosition = viewport,
        });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async System.Threading.Tasks.Task ComputeBuddyLabStateAsync(
        Dictionary<string, bool> state,
        ulong seed,
        int timeoutPhysicsTicks,
        JsonElement journey)
    {
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
        {
            state["lab_composed"] = false;
            state["lab_six_body"] = false;
            state["lab_finite"] = false;
            state["lab_settled"] = false;
            state["lab_telemetry_visible"] = false;
            return;
        }

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        GetTree().Root.AddChild(lab);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);

        string? exercise = null;
        journey.TryGetProperty("setup", out JsonElement exerciseSetup);
        if (exerciseSetup.ValueKind == JsonValueKind.Object &&
            exerciseSetup.TryGetProperty("exercise", out JsonElement exerciseElement))
            exercise = exerciseElement.GetString();

        if (exercise == "grab_throw_all_parts")
        {
            await ExerciseGrabThrowAsync(state, lab);
        }
        else if (exercise == "walk_jump")
        {
            await ExerciseWalkJumpAsync(state, lab, timeoutPhysicsTicks);
        }
        else if (exercise == "idle_soak")
        {
            int ticks = IdleSoakScenario.FullTicks;
            if (exerciseSetup.TryGetProperty("advance_ticks", out JsonElement advance) && advance.TryGetInt32(out int configured)) ticks = configured;
            await ExerciseIdleSoakAsync(state, lab, ticks);
        }
        else if (exercise == "m3_glove_strike")
        {
            await ExerciseM3GloveStrikeAsync(state, lab);
        }
        else if (exercise == "m3_tool_feel")
        {
            await ExerciseM3ToolFeelAsync(state, lab);
        }
        else if (exercise == "m35_presentation_toggle")
        {
            await ExerciseM35PresentationToggleAsync(state, lab);
        }
        else if (exercise == "m36_expressive")
        {
            await ExerciseM36ExpressiveAsync(state, lab, timeoutPhysicsTicks);
        }
        else if (exercise == "m5_meal")
        {
            await ExerciseM5MealAsync(state, lab);
        }
        else if (exercise == "m5_baseball_bat")
        {
            await ExerciseM5BaseballBatAsync(state, lab);
        }
        else if (exercise == "m5_homerun_bat")
        {
            await ExerciseM5HomeRunBatAsync(state, lab);
        }
        else if (exercise == "m5_pistol")
        {
            await ExerciseM5PistolAsync(state, lab);
        }
        else if (exercise == "m5_grenade")
        {
            await ExerciseM5GrenadeAsync(state, lab);
        }
        else if (exercise is null && journey.TryGetProperty("steps", out JsonElement steps) &&
                 steps.ValueKind == JsonValueKind.Array && steps.GetArrayLength() > 0)
        {
            await ExecuteStepsAsync(state, lab, steps);
        }

        bool finite = true;
        bool settled = false;
        for (int tick = 0; tick < timeoutPhysicsTicks; tick++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            finite &= lab.Buddy.Rig.AllBodiesFinite();
            if (lab.Buddy.Standing.Snapshot.IsStable)
            {
                settled = true;
                break;
            }
        }

        bool linkTelemetryFinite = true;
        foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry)
        {
            linkTelemetryFinite &= float.IsFinite(link.Strain) && link.ForceOnA.IsFinite();
        }

        state["lab_composed"] = lab.IsInsideTree() && lab.Buddy.IsInitialized;
        state["lab_six_body"] = lab.Buddy.Rig.Parts.Count == PuppetRigProfile.RequiredPartCount;
        state["lab_finite"] = finite && linkTelemetryFinite;
        state["lab_settled"] = settled;
        state["lab_telemetry_visible"] = lab.TelemetryPanel.IsInitialized && lab.TelemetryPanel.Visible;
        lab.QueueFree();
        // QueueFree completes on the next idle frame. Let component teardown run
        // before the runner writes its verdict and quits. Dynamic Godot Resources
        // such as cursor-tool shapes also have managed wrappers; collect those
        // while the native runtime is live, then give the rendering/physics
        // servers one more frame to release their final RIDs.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GodotInteropShutdown.PrepareForQuit();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// <summary>
    /// The M5 Meal slice through real input, happy path and cancel path: key `6` places one
    /// Meal, the ordinary Grab tether picks it up, a secondary tap without a pull returns to
    /// carrying, a pull-back release launches it, and the buddy then fetches and eats it for
    /// its authored mood gain and cooldown.
    /// </summary>
    private async System.Threading.Tasks.Task ExerciseM5MealAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        SceneTree tree = GetTree();
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        // Place it on the far side of the room so the launch has somewhere to travel and the
        // buddy has to walk to what lands.
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;
        Vector2 spawn = new(
            Mathf.Clamp(torso.X + (side * 130.0f), room.Position.X + 130.0f, room.End.X - 130.0f),
            Mathf.Clamp(torso.Y, room.Position.Y + 40.0f, room.End.Y - 40.0f));

        ToolId toolBefore = lab.Pipeline.SelectedTool;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, spawn, 0);
        await M4ObjectScenarioSupport.SendKey(tree, Key.Key6);
        await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Launcher.HasLaunchable && lab.Objects.Count == 1, 20);
        LooseObjectBody? meal = lab.Launcher.CurrentLaunchable;
        state["meal_key_spawns_one_meal"] =
            GodotObject.IsInstanceValid(meal) &&
            meal!.SemanticContentId == ContentIds.ToolMeal &&
            lab.Objects.Count == 1 &&
            lab.Pipeline.SelectedTool == toolBefore &&
            !lab.Grab.IsGrabbing;

        Vector2 pick = GodotObject.IsInstanceValid(meal) ? meal!.GlobalPosition : spawn;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, pick, 0);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Left, pressed: true, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == meal, 30);
        state["meal_is_carried_by_the_normal_grab"] =
            lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == meal;

        // Cancel path: secondary down and straight back up, with no pull, keeps the carry.
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Right, pressed: true,
            MouseButtonMask.Left | MouseButtonMask.Right);
        await M4ObjectScenarioSupport.WaitFor(tree, () => lab.Launcher.IsAiming, 30);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Right, pressed: false, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(tree, () => !lab.Launcher.IsAiming, 30);
        state["meal_aim_cancel_keeps_the_carry"] =
            lab.Grab.IsGrabbing &&
            lab.Grab.CurrentGrab.Target == meal &&
            lab.Launcher.CancelCount >= 1 &&
            lab.Launcher.LaunchCount == 0;

        // Happy path: aim, pull back away from the buddy, release to launch toward it.
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Right, pressed: true,
            MouseButtonMask.Left | MouseButtonMask.Right);
        await M4ObjectScenarioSupport.WaitFor(tree, () => lab.Launcher.IsAiming, 30);
        Vector2 pull = pick + new Vector2(side * 70.0f, -20.0f);
        await M4ObjectScenarioSupport.MovePointer(
            tree, lab, pull, MouseButtonMask.Left | MouseButtonMask.Right);
        for (int tick = 0; tick < 20; tick++)
            await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pull, MouseButton.Right, pressed: false, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(tree, () => lab.Launcher.LaunchCount == 1, 60);
        state["meal_pullback_launches_it"] =
            lab.Launcher.LaunchCount == 1 &&
            lab.Launcher.LastLaunchVelocity.Length() > 100.0f &&
            !lab.Grab.IsGrabbing;

        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pull, MouseButton.Left, pressed: false, 0);

        float moodBefore = lab.Progress.Mood;
        float fullnessBefore = lab.Progress.Fullness;
        bool eaten = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Buddy.ObjectInteraction.ConsumeSuccessCount == 1, 3600);
        state["buddy_fetches_and_eats_the_meal"] = eaten;
        state["meal_pays_its_authored_mood"] =
            eaten && lab.Progress.Mood >= moodBefore + 9.5f;
        state["meal_fills_the_hunger_bar"] =
            eaten && lab.Progress.Fullness >= fullnessBefore + 49.5f;

        Log.Info(
            "Journey",
            $"M5 meal launches={lab.Launcher.LaunchCount} cancels={lab.Launcher.CancelCount} " +
            $"successes={lab.Buddy.ObjectInteraction.ConsumeSuccessCount} " +
            $"mood={lab.Progress.Mood:F1} fullness={lab.Progress.Fullness:F1}");
    }

    /// <summary>
    /// The M5 Baseball Bat slice through real input: the lab's tool key selects the bat,
    /// the real pointer gives it its cursor, a swing across the buddy scores pain
    /// attributed to the bat and teaches the buddy to fear it specifically, and
    /// selecting another tool takes the collider away again.
    /// </summary>
    private async System.Threading.Tasks.Task ExerciseM5BaseballBatAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        SceneTree tree = GetTree();
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;

        await M4ObjectScenarioSupport.SendKey(tree, Key.K);
        Vector2 windUp = new(
            Mathf.Clamp(torso.X - (side * 150.0f), room.Position.X + 60.0f, room.End.X - 60.0f),
            Mathf.Clamp(torso.Y, room.Position.Y + 60.0f, room.End.Y - 60.0f));
        await M4ObjectScenarioSupport.MovePointer(tree, lab, windUp, 0);
        bool spawned = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.CursorTools.IsActive, 30);
        CursorToolBody? bat = lab.CursorTools.Body;
        state["bat_key_selects_the_bat"] =
            lab.Pipeline.SelectedTool == ToolId.BaseballBat &&
            spawned &&
            bat is not null &&
            bat.ContentId == ContentIds.ToolBaseballBat &&
            bat.IsElongated;

        bool tracked = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => GodotObject.IsInstanceValid(bat) &&
                  bat!.GlobalPosition.DistanceTo(lab.CursorTools.Cursor) <= 30.0f,
            60);
        state["bat_follows_the_real_pointer"] = tracked;

        AcceptedImpact? batImpact = null;
        void OnImpact(AcceptedImpact impact)
        {
            if (batImpact is null && impact.ContentId == ContentIds.ToolBaseballBat)
                batImpact = impact;
        }
        lab.Pipeline.ImpactAccepted += OnImpact;

        // The swing is pointer motion at real speed; the impulse is whatever the
        // solver measures out of it, never an authored number.
        Vector2 swing = windUp;
        float step = 20.0f;
        for (int tick = 0; tick < 60 && batImpact is null; tick++)
        {
            swing = new Vector2(
                Mathf.Clamp(swing.X + (side * step), room.Position.X + 20.0f, room.End.X - 20.0f),
                swing.Y);
            await M4ObjectScenarioSupport.MovePointer(tree, lab, swing, 0);
        }

        for (int tick = 0; tick < 60 && batImpact is null; tick++)
            await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        lab.Pipeline.ImpactAccepted -= OnImpact;

        state["bat_swing_hurts_the_buddy"] =
            batImpact is { Pain: > 0.0f } hit && hit.ContentId == ContentIds.ToolBaseballBat;
        state["bat_is_remembered_as_harmful"] =
            lab.Progress.IsContentHarmful(ContentIds.ToolBaseballBat) &&
            !lab.Progress.IsContentHarmful(ContentIds.ToolBoxingGlove);

        await M4ObjectScenarioSupport.SendKey(tree, Key.G);
        bool despawned = await M4ObjectScenarioSupport.WaitFor(
            tree, () => !lab.CursorTools.IsActive, 30);
        state["selecting_grab_takes_the_bat_away"] =
            lab.Pipeline.SelectedTool == ToolId.Grab && despawned;

        Log.Info(
            "Journey",
            $"M5 bat pain={batImpact?.Pain:F2} impulse={batImpact?.Impulse:F1} " +
            $"part={batImpact?.Part} harmful={lab.Progress.IsContentHarmful(ContentIds.ToolBaseballBat)}");
    }

    /// <summary>
    /// Promoted Task G trace: select the bat with its real key, acquire the
    /// handle with primary, charge through exactly 600 routed physics ticks with
    /// secondary, then release through a semantic torso-derived contact arc.
    /// No component is commanded directly; all player intent enters through the
    /// same queued key/pointer/button events as ordinary play.
    /// </summary>
    private async System.Threading.Tasks.Task ExerciseM5HomeRunBatAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        SceneTree tree = GetTree();
        // The development telemetry panel occupies the left contact zone and
        // consumes mouse buttons there. Hide it through its real lab key just
        // as the Task G trace did; the journey must exercise the game, not a
        // Control overlay sitting above it.
        await M4ObjectScenarioSupport.SendKey(tree, Key.H);
        await M4ObjectScenarioSupport.SendKey(tree, Key.K);
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torsoAtSelection = lab.Buddy.Rig.Torso.GlobalPosition;
        Vector2 openSpawn = new(
            Mathf.Clamp(torsoAtSelection.X - 150.0f, room.Position.X + 40.0f, room.End.X - 40.0f),
            Mathf.Clamp(torsoAtSelection.Y - 150.0f, room.Position.Y + 40.0f, room.End.Y - 40.0f));
        await M4ObjectScenarioSupport.MovePointer(
            tree, lab, openSpawn, 0);
        bool spawned = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.CursorTools.IsActive,
            30);

        CursorToolProfile? profile = lab.CursorTools.ActiveProfile;
        CursorToolBody? bat = lab.CursorTools.Body;
        state["homerun_key_selects_swing_bat"] =
            lab.Pipeline.SelectedTool == ToolId.BaseballBat &&
            spawned &&
            profile?.Swing is not null &&
            bat is { IsElongated: true };
        if (profile?.Swing is not { } swing || bat is null)
        {
            return;
        }

        SwingPlan plan = ChargedSwing.SwingPlanFor(
            1.0f,
            profile.HandleToTipRadius,
            swing.ToConstants());
        SwingTrajectoryPoint contact = ChargedSwing.SwingTrajectoryAt(
            plan.WindupTicks +
            Mathf.Clamp(Mathf.RoundToInt(plan.SweepTicks * 0.50f), 0, plan.SweepTicks - 1),
            plan,
            directionSign: 1,
            swing.ToConstants());
        Vector2 tipFromPivot =
            new Vector2(0.0f, -profile.HandleToTipRadius).Rotated(contact.BarrelAngle);
        Vector2 pivot = lab.Buddy.Rig.Torso.GlobalPosition - tipFromPivot;

        // The final significant pointer travel is rightward, so the semantic
        // direction resolver commits the canonical right swing at release.
        Vector2 prePivot = pivot - Vector2.Right * 12.0f;
        await M4ObjectScenarioSupport.MovePointer(
            tree, lab, prePivot, 0);
        await WaitPhysicsTicks(tree, 120);
        await SetInputActionAsync(tree, InputActions.Primary, pressed: true);
        bool gripped = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.CursorTools.SwingState == ChargedSwingState.Gripped,
            120);
        Log.Info(
            "Journey",
            $"M5 home-run grip probe gripped={gripped} panel={lab.TelemetryPanel.Visible} " +
            $"pointer_active={lab.Pointer.IsActive} pointer_seen={lab.Pointer.HasPointerInput} " +
            $"primary={lab.Pointer.IsPrimaryHeld} input={lab.Pointer.ReceivedInputCount} " +
            $"swing={lab.CursorTools.SwingState} cursor={lab.CursorTools.Cursor}");
        await M4ObjectScenarioSupport.MovePointer(
            tree, lab, pivot, MouseButtonMask.Left);
        await WaitPhysicsTicks(tree, 60);

        int homeRunImpactCount = 0;
        AcceptedImpact homeRunImpact = default;
        Vector2 centerAtImpact = Vector2.Zero;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId != ContentIds.ToolBaseballBat ||
                impact.SwingEpoch <= 0 ||
                impact.Pain <= 0.0f)
            {
                return;
            }

            homeRunImpactCount++;
            homeRunImpact = impact;
            if (homeRunImpactCount == 1)
            {
                centerAtImpact = WholeBuddyCenter(lab);
            }
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        await SetInputActionAsync(tree, InputActions.Secondary, pressed: true);
        // The press frame enters CHARGING at tick zero; these are the exact 600
        // routed charge ticks, not a wall-clock sleep.
        bool enteredCharging = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.CursorTools.SwingState == ChargedSwingState.Charging,
            3);
        long routedAtChargeStart = lab.Buddy.RoutedTicks;
        bool reachedChargeCap = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.CursorTools.SwingChargeTicks == swing.MaxChargeTicks,
            swing.MaxChargeTicks + 2);
        long routedChargeTicks = lab.Buddy.RoutedTicks - routedAtChargeStart;
        int chargeTicksAtRelease = lab.CursorTools.SwingChargeTicks;
        float chargeAtRelease = lab.CursorTools.SwingCharge;
        int glintStartsAtRelease = bat.ChargeGlintStarts;

        // Refresh the semantic contact point against the buddy's live torso
        // after the five-second hold. Autonomy may have walked; the journey
        // follows the target instead of relying on a stale pixel.
        tipFromPivot =
            new Vector2(0.0f, -profile.HandleToTipRadius).Rotated(contact.BarrelAngle);
        pivot = lab.Buddy.Rig.Torso.GlobalPosition - tipFromPivot;
        prePivot = pivot - Vector2.Right * 12.0f;
        await M4ObjectScenarioSupport.MovePointer(
            tree,
            lab,
            prePivot,
            MouseButtonMask.Left | MouseButtonMask.Right);
        await WaitPhysicsTicks(tree, 30);
        await M4ObjectScenarioSupport.MovePointer(
            tree,
            lab,
            pivot,
            MouseButtonMask.Left | MouseButtonMask.Right);

        await SetInputActionAsync(tree, InputActions.Secondary, pressed: false);
        bool releaseCommitted = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.CursorTools.SwingEpoch > 0,
            3);
        int releasedEpoch = lab.CursorTools.SwingEpoch;
        bool hit = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => homeRunImpactCount > 0,
            120);
        bool freezeCompleted = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.SwingHitLag.CompletionCount == 1,
            120);
        long routedAtCompletion = lab.Buddy.RoutedTicks;
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Buddy.RoutedTicks >= routedAtCompletion + 12,
            120);
        bool launchResumed =
            hit && WholeBuddyCenter(lab).DistanceTo(centerAtImpact) > 0.1f;
        bool sawRecovery = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.CursorTools.SwingState == ChargedSwingState.Recovery,
            120);
        bool returnedToGrip = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.CursorTools.SwingState == ChargedSwingState.Gripped,
            120);
        await SetInputActionAsync(tree, InputActions.Primary, pressed: false);
        lab.Pipeline.ImpactAccepted -= OnImpact;

        state["homerun_grips_by_the_handle"] = gripped;
        state["homerun_charge_is_exactly_600_ticks"] =
            reachedChargeCap &&
            enteredCharging &&
            routedChargeTicks == swing.MaxChargeTicks &&
            chargeTicksAtRelease == swing.MaxChargeTicks &&
            Mathf.IsEqualApprox(chargeAtRelease, 1.0f) &&
            glintStartsAtRelease == 3;
        state["homerun_scores_one_attributed_impact"] =
            hit &&
            releaseCommitted &&
            homeRunImpactCount == 1 &&
            homeRunImpact.ContentId == ContentIds.ToolBaseballBat &&
            homeRunImpact.SwingEpoch == releasedEpoch;
        state["homerun_whole_game_freeze_completes"] =
            freezeCompleted &&
            lab.SwingHitLag.TriggerCount == 1 &&
            lab.SwingHitLag.TotalTicks == swing.HitLagMaxTicks &&
            lab.SwingHitLag.FrozenFrameCount == swing.HitLagMaxTicks;
        state["homerun_launch_resumes_after_freeze"] = launchResumed;
        state["homerun_recovers_to_gripped"] = sawRecovery && returnedToGrip;

        Log.Info(
            "Journey",
            $"M5 home-run charge={chargeTicksAtRelease}/{swing.MaxChargeTicks} " +
            $"routed={routedChargeTicks} " +
            $"epoch={releasedEpoch} impacts={homeRunImpactCount} " +
            $"impulse={homeRunImpact.Impulse:F1} freeze=(" +
            $"{lab.SwingHitLag.TriggerCount},{lab.SwingHitLag.FrozenFrameCount}) " +
            $"recovery={sawRecovery}/{returnedToGrip} resumed={launchResumed}");
    }

    /// <summary>
    /// The M5 Grenade slice end to end, through real input: the shop still refuses one and
    /// an unowned spawn key places nothing, a buddy that has never met a grenade is curious
    /// and catches a pinned one like a ball, the pullback chord's first secondary press
    /// pulls the pin and the throw starts the three-second fuse, the blast hurts the buddy
    /// through the shared curve and teaches it that grenades are harmful, and the next one
    /// is left strictly alone.
    /// </summary>
    private async System.Threading.Tasks.Task ExerciseM5GrenadeAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        SceneTree tree = GetTree();
        GrenadeComponent grenades = lab.Grenades;
        LooseObjectProfile? grenadeProfile = FindLaunchable(lab, ContentIds.ToolGrenade);
        Rect2 room = lab.Boundaries.InnerBounds;
        Vector2 torso = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = torso.X <= room.GetCenter().X ? 1.0f : -1.0f;

        // --- The shop will not sell one yet ---
        // The catalogue entry stays `Visible = false` until the owner's feel gate, and the
        // shop refuses it for exactly that reason rather than for the balance. Ownership
        // in this journey comes from the development laboratory catalogue, the same way
        // every other unreleased M5 tool is granted; when the owner flips the entry
        // visible this refusal becomes a real purchase.
        PurchaseResult refused = lab.Economy.Purchase(ContentIds.ToolGrenade);
        Vector2 bench = new(
            Mathf.Clamp(torso.X + (side * 140.0f), room.Position.X + 130.0f, room.End.X - 130.0f),
            Mathf.Clamp(torso.Y, room.Position.Y + 40.0f, room.End.Y - 40.0f));

        state["the_grenade_is_not_on_sale_until_the_owner_gates_it"] =
            !refused.Succeeded &&
            refused.Status == PurchaseStatus.NotAvailable &&
            grenadeProfile is not null &&
            lab.Progress.IsToolUnlocked(ContentIds.ToolGrenade) &&
            lab.Objects.Count == 0;

        Log.Info(
            "Journey",
            $"M5 grenade gate: refused={refused.Status} succeeded={refused.Succeeded} " +
            $"owned={lab.Progress.IsToolUnlocked(ContentIds.ToolGrenade)} " +
            $"objects={lab.Objects.Count} profile={grenadeProfile?.ContentId ?? "<none>"}");

        // --- Curious: a buddy that has never met a grenade catches a pinned one ---
        LooseObjectBody? gift = M4ObjectScenarioSupport.SpawnCleanThrow(
            lab, profile: grenadeProfile);
        bool caught = await M4ObjectScenarioSupport.WaitForPhase(
            tree, lab, ObjectPhase.Hold, 600);
        state["curious_buddy_catches_an_unfamiliar_grenade"] =
            gift is not null &&
            caught &&
            grenades.Tracked == gift &&
            grenades.Stage == GrenadeFuseStage.Pinned &&
            grenades.DetonationCount == 0 &&
            !lab.Pipeline.IsToolHarmful(ContentIds.ToolGrenade);

        // --- The spawn key, the pin, and the throw ---
        await M4ObjectScenarioSupport.MovePointer(tree, lab, bench, 0);
        await M4ObjectScenarioSupport.SendKey(tree, Key.Key7);
        await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => lab.Launcher.CurrentLaunchableContentId == ContentIds.ToolGrenade &&
                  grenades.Tracked is not null,
            30);
        LooseObjectBody? live = grenades.Tracked;
        state["the_grenade_key_places_one_owned_grenade"] =
            live is not null &&
            live.SemanticContentId == ContentIds.ToolGrenade &&
            lab.Objects.Count == 1 &&
            grenades.Stage == GrenadeFuseStage.Pinned &&
            !lab.Grab.IsGrabbing;
        if (live is null)
            return;

        // Let it fall to the floor and settle before reaching for it: the spawn key places
        // it at the pointer, in mid-air, and a click aimed at where it was born misses.
        for (int tick = 0; tick < 90; tick++)
            await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);

        Vector2 pick = live.GlobalPosition;
        await M4ObjectScenarioSupport.MovePointer(tree, lab, pick, 0);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Left, pressed: true, MouseButtonMask.Left);
        await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Grab.IsGrabbing && lab.Grab.CurrentGrab.Target == live, 60);

        int pinsBefore = grenades.PinDropCount;
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pick, MouseButton.Right, pressed: true,
            MouseButtonMask.Left | MouseButtonMask.Right);
        await M4ObjectScenarioSupport.WaitFor(tree, () => lab.Launcher.IsAiming, 30);
        bool pinOutWhileHeld = grenades.PinIsOut && !grenades.IsCountingDown;

        // Pull back away from the buddy and let go: a short pull, so it lands near the
        // buddy rather than crossing the room. This is the Baseball's chord exactly.
        Vector2 pull = pick + new Vector2(side * 34.0f, -14.0f);
        await M4ObjectScenarioSupport.MovePointer(
            tree, lab, pull, MouseButtonMask.Left | MouseButtonMask.Right);
        for (int tick = 0; tick < 20; tick++)
            await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pull, MouseButton.Right, pressed: false, MouseButtonMask.Left);
        bool launched = await M4ObjectScenarioSupport.WaitFor(
            tree, () => lab.Launcher.LaunchCount >= 1 && grenades.IsCountingDown, 60);
        long releaseTick = lab.Controls.RoutedPhysicsTicks;
        await M4ObjectScenarioSupport.SetButton(
            tree, lab, pull, MouseButton.Left, pressed: false, 0);

        Log.Info(
            "Journey",
            $"M5 grenade throw: pins={grenades.PinDropCount - pinsBefore} " +
            $"pin_out_while_held={pinOutWhileHeld} launched={launched} " +
            $"counting={grenades.IsCountingDown} remaining={grenades.FuseTicksRemaining} " +
            $"grabbing={lab.Grab.IsGrabbing} launches={lab.Launcher.LaunchCount}");

        state["the_pullback_throw_pulls_the_pin_and_starts_the_fuse"] =
            grenades.PinDropCount == pinsBefore + 1 &&
            pinOutWhileHeld &&
            launched &&
            grenades.IsCountingDown &&
            grenades.FuseTicksRemaining > 0 &&
            !lab.Grab.IsGrabbing;

        // --- It goes off on its own, and it hurts ---
        float blastPain = 0.0f;
        long blastMilli = 0;
        void OnImpact(AcceptedImpact impact)
        {
            if (impact.ContentId != ContentIds.ToolGrenade)
                return;
            blastPain += impact.Pain;
            blastMilli += impact.MilliCredits;
        }

        lab.Pipeline.ImpactAccepted += OnImpact;
        long balanceBefore = lab.Progress.BalanceMilliCredits;
        int detonationsBefore = grenades.DetonationCount;
        bool detonated = await M4ObjectScenarioSupport.WaitFor(
            tree, () => grenades.DetonationCount > detonationsBefore, 600);
        long blastTick = lab.Controls.RoutedPhysicsTicks;
        for (int tick = 0; tick < 30; tick++)
            await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        lab.Pipeline.ImpactAccepted -= OnImpact;

        state["the_thrown_grenade_explodes_three_seconds_later"] =
            detonated &&
            blastTick - releaseTick <= grenades.Profile.FuseTicks + 4 &&
            blastTick - releaseTick >= grenades.Profile.FuseTicks - 8 &&
            lab.Objects.Count == 0;
        state["the_blast_hurts_the_buddy_and_pays_for_it"] =
            blastPain > 0.0f &&
            blastMilli > 0 &&
            lab.Progress.BalanceMilliCredits > balanceBefore;
        state["the_grenade_is_remembered_as_harmful"] =
            lab.Pipeline.IsToolHarmful(ContentIds.ToolGrenade);

        // --- And the next one is left strictly alone ---
        await M4ObjectScenarioSupport.WaitFor(
            tree, () => !lab.Pipeline.LastKnockoutState.KnockoutActive, 900);
        int catchesBefore = lab.Buddy.ObjectInteraction.CleanCatchCount;
        LooseObjectBody? feared = M4ObjectScenarioSupport.SpawnCleanThrow(
            lab, profile: grenadeProfile);
        bool everHeld = false;
        for (int tick = 0; tick < 600; tick++)
        {
            await ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            everHeld |= lab.Buddy.ObjectInteraction.IsHolding;
        }

        state["harmed_buddy_leaves_the_next_grenade_alone"] =
            feared is not null &&
            !everHeld &&
            lab.Buddy.ObjectInteraction.CleanCatchCount == catchesBefore &&
            grenades.DetonationCount == detonationsBefore + 1;

        Log.Info(
            "Journey",
            $"M5 grenade pins={grenades.PinDropCount} detonations={grenades.DetonationCount} " +
            $"fuse={blastTick - releaseTick} ticks blast_pain={blastPain:F2} " +
            $"milli={blastMilli} harmful={lab.Pipeline.IsToolHarmful(ContentIds.ToolGrenade)} " +
            $"caught_first={caught} held_second={everHeld}");
    }

    /// <summary>The launcher's authored profile for one launchable, or <c>null</c>.</summary>
    private static LooseObjectProfile? FindLaunchable(BuddyLab lab, string contentId)
    {
        foreach (LooseObjectProfile profile in lab.Launcher.LaunchableProfiles)
        {
            if (GodotObject.IsInstanceValid(profile) && profile.ContentId == contentId)
                return profile;
        }

        return null;
    }

    /// <summary>
    /// The M5 Pistol slice through real input, happy path and reload path: the lab's
    /// tool key draws the pistol, pointer motion aims it, a wheel notch offsets that aim
    /// until the next motion clears it, one primary press fires exactly one shot whose
    /// projectile hurts the buddy and is remembered as the pistol, the <c>R</c> action
    /// reloads a partial magazine, an emptied magazine dry-fires into an automatic
    /// reload, and selecting Grab puts the gun away.
    /// </summary>
    private async System.Threading.Tasks.Task ExerciseM5PistolAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        SceneTree tree = GetTree();
        // The development telemetry panel covers the left contact zone and consumes
        // mouse buttons there; its own lab key hides it, as the home-run trace does.
        await M4ObjectScenarioSupport.SendKey(tree, Key.H);
        await M4ObjectScenarioSupport.SendKey(tree, Key.J);

        CursorGunComponent gun = lab.CursorGuns;

        // Aim is pointer travel, so the cursor arrives heading toward the buddy.
        Vector2 forward = await AimAtPointAsync(tree, lab, lab.Buddy.Rig.Torso.GlobalPosition, 140.0f);
        bool armed = await M4ObjectScenarioSupport.WaitFor(tree, () => gun.IsActive, 30);
        GunProfile? profile = gun.ActiveProfile;
        state["pistol_key_draws_a_loaded_pistol"] =
            lab.Pipeline.SelectedTool == ToolId.Pistol &&
            armed &&
            profile is not null &&
            gun.ActiveContentId == ContentIds.ToolPistol &&
            gun.RoundsRemaining == profile.MagazineCapacity;
        if (profile is null)
            return;

        state["pointer_motion_aims_the_pistol"] = gun.AimForward.Dot(forward) > 0.95f;

        // The wheel offsets the aim upward, and travelling again clears it. The offset
        // belongs to a hand that has stopped moving, so the aim is allowed to settle first —
        // scrolling mid-sweep would be cleared again by the sweep it is part of.
        Vector2 held = lab.Pointer.WorldCursor;
        await M4ObjectScenarioSupport.WaitFor(tree, () => !gun.AimIsSteering, 300);
        await M4ObjectScenarioSupport.SendWheel(tree, lab, held, up: true);
        await M4ObjectScenarioSupport.SendWheel(tree, lab, held, up: true);
        bool raised = gun.AimOffsetDegrees > 0.0f && gun.AimForward.Y < -0.05f;
        forward = await AimAtPointAsync(tree, lab, lab.Buddy.Rig.Torso.GlobalPosition, 140.0f);
        state["the_wheel_offsets_aim_until_the_next_motion"] =
            raised && Mathf.IsZeroApprox(gun.AimOffsetDegrees) &&
            gun.AimForward.Dot(forward) > 0.95f;

        AcceptedImpact? shotImpact = null;
        void OnImpact(AcceptedImpact impact)
        {
            if (shotImpact is null && impact.ContentId == ContentIds.ToolPistol)
                shotImpact = impact;
        }

        lab.Pipeline.ImpactAccepted += OnImpact;

        // Track the target the way a player does: the buddy has been walking about
        // since the tool was drawn, so the aim is taken from where its body is now.
        await AimAtBuddyAsync(tree, lab);

        // One press, one shot — and holding the button down never fires a second.
        int shotsBefore = gun.ShotCount;
        int launchedBefore = gun.ProjectilesLaunched;
        await SetInputActionAsync(tree, InputActions.Primary, pressed: true);
        await WaitPhysicsTicks(tree, profile.ShotIntervalTicks * 3);
        int firedWhileHeld = gun.ShotCount - shotsBefore;
        await SetInputActionAsync(tree, InputActions.Primary, pressed: false);
        state["one_press_fires_exactly_one_shot"] =
            firedWhileHeld == 1 &&
            gun.ProjectilesLaunched - launchedBefore == 1 &&
            gun.RoundsRemaining == profile.MagazineCapacity - 1;

        // Then keep shooting until one lands, re-aiming at a buddy that is moving,
        // recoiling, and being knocked about — the same thing a player does, and the
        // reason this is a "does a hit hurt" assertion rather than a marksmanship one.
        for (int shot = 0; shot < 5 && shotImpact is null; shot++)
        {
            await AimAtBuddyAsync(tree, lab);
            Log.Info(
                "Journey",
                $"M5 pistol aimed shot {shot}: cursor={lab.Pointer.WorldCursor} " +
                $"aim={gun.AimForward} torso={lab.Buddy.Rig.Torso.GlobalPosition} " +
                $"rounds={gun.RoundsRemaining}");
            await SetInputActionAsync(tree, InputActions.Primary, pressed: true);
            await SetInputActionAsync(tree, InputActions.Primary, pressed: false);
            await M4ObjectScenarioSupport.WaitFor(
                tree, () => shotImpact is not null, profile.ShotIntervalTicks);
            Log.Info(
                "Journey",
                $"M5 pistol after shot {shot}: raw={lab.Pipeline.MaxRawImpulse:F1} " +
                $"scored={lab.Pipeline.ScoredImpactCount} flying={gun.ActiveProjectileCount} " +
                $"torso={lab.Buddy.Rig.Torso.GlobalPosition} " +
                $"head={lab.Buddy.Rig.Head.GlobalPosition}");
        }

        lab.Pipeline.ImpactAccepted -= OnImpact;
        state["a_pistol_shot_hurts_the_buddy"] =
            shotImpact is { Pain: > 0.0f } landed &&
            landed.ContentId == ContentIds.ToolPistol &&
            landed.MilliCredits > 0L;
        state["the_pistol_is_remembered_as_harmful"] =
            lab.Progress.IsContentHarmful(ContentIds.ToolPistol) &&
            !lab.Progress.IsContentHarmful(ContentIds.ToolBaseballBat);

        // The R action reloads a partial magazine through the same queued-input path.
        int completionsBefore = gun.ReloadCompleteCount;
        await M4ObjectScenarioSupport.SendKey(tree, Key.R);
        bool reloadRunning = await M4ObjectScenarioSupport.WaitFor(
            tree, () => gun.IsReloading, 10);
        bool refilled = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => gun.ReloadCompleteCount == completionsBefore + 1 &&
                  gun.RoundsRemaining == profile.MagazineCapacity,
            profile.ReloadTicks + 30);
        state["the_reload_key_refills_a_partial_magazine"] = reloadRunning && refilled;

        // Empty the magazine, then pull once more: the dry fire is what reloads.
        for (int shot = 0; shot < profile.MagazineCapacity; shot++)
        {
            await SetInputActionAsync(tree, InputActions.Primary, pressed: true);
            await SetInputActionAsync(tree, InputActions.Primary, pressed: false);
            await WaitPhysicsTicks(tree, profile.ShotIntervalTicks);
        }

        bool emptied = gun.RoundsRemaining == 0 && !gun.IsReloading;
        int dryBefore = gun.DryFireCount;
        await SetInputActionAsync(tree, InputActions.Primary, pressed: true);
        await SetInputActionAsync(tree, InputActions.Primary, pressed: false);
        state["an_empty_magazine_dry_fires_into_an_automatic_reload"] =
            emptied && gun.DryFireCount == dryBefore + 1 && gun.IsReloading;

        bool autoRefilled = await M4ObjectScenarioSupport.WaitFor(
            tree,
            () => !gun.IsReloading && gun.RoundsRemaining == profile.MagazineCapacity,
            profile.ReloadTicks + 30);
        state["the_automatic_reload_completes"] = autoRefilled;

        await M4ObjectScenarioSupport.SendKey(tree, Key.G);
        bool holstered = await M4ObjectScenarioSupport.WaitFor(tree, () => !gun.IsActive, 30);
        state["selecting_grab_holsters_the_pistol"] =
            lab.Pipeline.SelectedTool == ToolId.Grab && holstered;

        Log.Info(
            "Journey",
            $"M5 pistol shots={gun.ShotCount} launched={gun.ProjectilesLaunched} " +
            $"dry={gun.DryFireCount} reloads={gun.ReloadCompleteCount} " +
            $"pain={shotImpact?.Pain:F2} impulse={shotImpact?.Impulse:F1} " +
            $"part={shotImpact?.Part} active_projectiles={gun.ActiveProjectileCount}");
    }

    /// <summary>
    /// Aims a cursor weapon at a world point from a stand-off and returns the direction its
    /// shot should travel. A cursor weapon aims by the direction the pointer has lately been
    /// travelling (RAGDOLL §9.1), so this walks the real pointer along that line rather than
    /// teleporting it into position.
    ///
    /// <para>Three details are load-bearing, and each of them broke this journey once. The
    /// approach is long enough for the aim to <b>slew</b> round from wherever it was
    /// pointing, because the aim turns at a bounded rate and a short run leaves it halfway.
    /// The jump to the start of that run is itself travel, so the aim is allowed to come to
    /// rest before the real approach begins. And the stand-off is taken on whichever side of
    /// the target has more room behind it, because a pointer that runs into the edge of the
    /// play area stops travelling — and an aim with no travel simply holds.</para>
    ///
    /// <para>The four-tick settle at the end is not padding either. A synthesized pointer
    /// event is delivered at the start of the engine's next iteration, so the physics frame
    /// awaited by <c>MovePointer</c> still runs on the previous cursor. Reading — or firing —
    /// right after the last move would use the aim from the move before it, which is how
    /// this journey first "missed" a point-blank shot that the game had aimed correctly.</para>
    /// </summary>
    private static async System.Threading.Tasks.Task<Vector2> AimAtPointAsync(
        SceneTree tree,
        BuddyLab lab,
        Vector2 target,
        float standOff = 70.0f)
    {
        const float StepPx = 1.5f;
        Rect2 room = lab.Boundaries.InnerBounds;
        float turnRate = lab.CursorGuns.ActiveProfile?.MaxAimTurnDegreesPerTick ?? 6.0f;
        int steps = (int)Mathf.Ceil(180.0f / Mathf.Max(0.5f, turnRate)) + 14;

        float side = target.X - room.Position.X >= room.End.X - target.X ? -1.0f : 1.0f;
        var forward = new Vector2(-side, 0.0f);
        Vector2 anchor = new(
            Mathf.Clamp(
                target.X + (side * standOff), room.Position.X + 8.0f, room.End.X - 8.0f),
            Mathf.Clamp(target.Y, room.Position.Y + 8.0f, room.End.Y - 8.0f));
        Vector2 start = anchor - (forward * (StepPx * steps));

        await M4ObjectScenarioSupport.MovePointer(tree, lab, start, 0);
        await M4ObjectScenarioSupport.WaitFor(tree, () => !lab.CursorGuns.AimIsSteering, 300);
        for (int step = 1; step <= steps; step++)
        {
            await M4ObjectScenarioSupport.MovePointer(
                tree, lab, start + (forward * (StepPx * step)), 0);
        }

        await WaitPhysicsTicks(tree, 4);
        return forward;
    }

    /// <summary>
    /// Aims at the buddy's head from close in, where a shot has the best chance of landing
    /// on the buddy that exists when it arrives rather than the one that was there when it
    /// was aimed.
    ///
    /// <para>The head and not the torso, because the buddy's hands hang beside its chest: a
    /// horizontal chest shot from beside it grazes a hand first, and a graze is not a miss
    /// that can be retried — the bullet spends itself on it. Measured on seed 7 as six
    /// aimed shots in a row reporting contacts of impulse 157–185, all of them under the
    /// shared pain curve's floor of 350, so the buddy was hit six times and hurt none.</para>
    ///
    /// <para>And close, because the buddy walks: from a stand-off of a head radius plus 40
    /// the shot lands within a tick or two of leaving the barrel. This is a "does a hit
    /// hurt" assertion, not a marksmanship one.</para>
    /// </summary>
    private static async System.Threading.Tasks.Task<Vector2> AimAtBuddyAsync(SceneTree tree, BuddyLab lab) =>
        await AimAtPointAsync(
            tree,
            lab,
            lab.Buddy.Rig.Head.GlobalPosition,
            // Plus the barrel: a round is born at the muzzle, and the drawn gun now reaches
            // most of a head-width ahead of the cursor. Standing off by the target distance
            // alone would spawn the shot past the head it is aimed at.
            lab.Buddy.Rig.Head.Radius + 40.0f +
                (lab.CursorGuns.ActiveProfile?.MuzzleOffsetPx ?? 0.0f));

    private static async System.Threading.Tasks.Task WaitPhysicsTicks(
        SceneTree tree,
        int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
        }
    }

    private static async System.Threading.Tasks.Task SetInputActionAsync(
        SceneTree tree,
        StringName action,
        bool pressed)
    {
        Input.ParseInputEvent(new InputEventAction
        {
            Action = action,
            Pressed = pressed,
            Strength = pressed ? 1.0f : 0.0f,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }

    private static Vector2 WholeBuddyCenter(BuddyLab lab)
    {
        Vector2 weighted = Vector2.Zero;
        float totalMass = 0.0f;
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            weighted += part.GlobalPosition * part.Mass;
            totalMass += part.Mass;
        }

        return totalMass > 0.0f ? weighted / totalMass : Vector2.Zero;
    }

    private async System.Threading.Tasks.Task ExerciseM35PresentationToggleAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        state["presentation_starts_mii3d"] =
            lab.Mode == PresentationMode.Mii3D &&
            lab.VisualPresenter.Visible && AllPartVisibility(lab, false);

        await PressVAsync();
        state["presentation_toggle_enters_legacy"] =
            lab.Mode == PresentationMode.LegacyCircles &&
            !lab.VisualPresenter.Visible && AllPartVisibility(lab, true);

        await PressVAsync();
        state["presentation_toggle_restores_mii3d"] =
            lab.Mode == PresentationMode.Mii3D &&
            lab.VisualPresenter.Visible && AllPartVisibility(lab, false);

        async System.Threading.Tasks.Task PressVAsync()
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.V, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.V, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        static bool AllPartVisibility(BuddyLab source, bool expected)
        {
            foreach (PuppetPartBody part in source.Buddy.Rig.Parts)
            {
                if (part.Visible != expected)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// M3.6 Task 6 composition journey: the expressive layer driven end-to-end through
    /// the laboratory's own dev keys — a walk turn-around in both directions, walk
    /// dressing while the buddy actually walks, and eat with a socketed item plus a wave.
    /// Everything asserted here is presentation state; no gameplay predicate moves.
    /// </summary>
    private async System.Threading.Tasks.Task ExerciseM36ExpressiveAsync(
        Dictionary<string, bool> state,
        BuddyLab lab,
        int timeoutPhysicsTicks)
    {
        // Deliberately mode-agnostic: the expressive layer is driven by the presenter's
        // process frame whether or not the 3D meshes are the visible presentation, so this
        // journey must produce identical verdicts under `--presentation=mii3d` and
        // `--presentation=legacy`. Asserting the MODE here would break that rule.
        state["expressive_layer_composed"] =
            lab.VisualPresenter.IsInitialized && lab.PosePipeline.IsInitialized &&
            lab.Facing.IsInitialized && lab.Activities.IsInitialized &&
            lab.HeadLookAt.IsInitialized && lab.Face.IsInitialized;

        // Walk dressing: wait for the seeded autonomy to actually walk, then the animator
        // must be on the walk clip with a phase that advanced from the frozen rest value.
        bool walked = false;
        bool walkClip = false;
        float phaseAtStart = lab.Activities.WalkPhase;
        bool phaseAdvanced = false;
        for (int tick = 0; tick < timeoutPhysicsTicks && !(walkClip && phaseAdvanced); tick++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            walked |= MathF.Abs(lab.Buddy.CurrentDriveIntent.WalkDirection) > 0.05f;
            if (lab.Activities.Current == ActivityId.WalkCycle)
            {
                walkClip = true;
                walkClip &= lab.Activities.CurrentClipName == ActivityAnimator.ClipNameFor(ActivityId.WalkCycle);
                phaseAdvanced |= !Mathf.IsEqualApprox(lab.Activities.WalkPhase, phaseAtStart);
            }
        }

        state["walked_during_journey"] = walked;
        state["walk_dressing_plays"] = walkClip && phaseAdvanced;

        // Turn-around, both ways, through the dev facing keys. The eased yaw must pass
        // through an intermediate magnitude: a snap would fail `facing_turn_is_eased`.
        (bool committed, bool eased) left = await TurnAsync(Key.Z, FacingSide.Left);
        (bool committed, bool eased) right = await TurnAsync(Key.X, FacingSide.Right);
        state["facing_turns_left"] = left.committed;
        state["facing_turns_right"] = right.committed;
        state["facing_turn_is_eased"] = left.eased && right.eased;

        await PressKeyAsync(Key.C);
        state["facing_release_returns_to_autonomy"] = lab.Facing.DevelopmentSide == 0;

        // Eat with an item in the hand: the key attaches the item visual, the eat clip
        // runs, the item rides the hand socket, and a second press clears both.
        await PressKeyAsync(Key.E);
        bool eating = false;
        bool itemRode = true;
        Node3D handSocket = lab.VisualPresenter.GetPartSocket(BuddyPartId.RightHand);
        for (int frame = 0; frame < 240 && !eating; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (lab.Activities.Current != ActivityId.Eat)
            {
                continue;
            }

            eating = lab.Activities.CurrentClipName == ActivityAnimator.ClipNameFor(ActivityId.Eat);
            Node3D leftHandSocket = lab.VisualPresenter.GetPartSocket(BuddyPartId.LeftHand);
            Vector3 handMidpoint = (leftHandSocket.GlobalPosition + handSocket.GlobalPosition) * 0.5f;
            itemRode &= lab.Activities.ItemSocket.GetChildCount() == 1 &&
                lab.Activities.ItemSocket.GlobalPosition.DistanceTo(handMidpoint) < 0.01f;
        }

        state["eat_key_attaches_item"] = lab.Controls.IsEatKeyItemAttached && itemRode;
        state["eat_clip_plays_with_item"] = eating;

        await PressKeyAsync(Key.E);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        state["eat_key_clears_item"] =
            !lab.Controls.IsEatKeyItemAttached && lab.Activities.Current != ActivityId.Eat;

        await PressKeyAsync(Key.Q);
        bool waved = false;
        for (int frame = 0; frame < 240 && !waved; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            waved = lab.Activities.Current == ActivityId.Wave &&
                lab.Activities.CurrentClipName == ActivityAnimator.ClipNameFor(ActivityId.Wave);
        }

        state["wave_key_plays_wave"] = waved;

        async System.Threading.Tasks.Task<(bool committed, bool eased)> TurnAsync(Key key, FacingSide side)
        {
            float before = lab.Facing.CurrentYawDegrees;
            await PressKeyAsync(key);
            bool sawIntermediate = false;
            bool arrived = false;
            float target = side == FacingSide.Right
                ? lab.Facing.Profile.FacingYawDegrees
                : -lab.Facing.Profile.FacingYawDegrees;
            for (int frame = 0; frame < 240 && !arrived; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                float yaw = lab.Facing.CurrentYawDegrees;
                // Strictly between the start and the target: proof the turn interpolated
                // rather than jumping in a single frame.
                sawIntermediate |= (yaw - before) * (target - yaw) > 0.0001f;
                arrived = lab.Facing.CommittedSide == side && Mathf.IsEqualApprox(yaw, target, 0.01f);
            }

            return (arrived, sawIntermediate);
        }

        async System.Threading.Tasks.Task PressKeyAsync(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async System.Threading.Tasks.Task ExerciseGrabThrowAsync(Dictionary<string, bool> state, BuddyLab lab)
    {
        bool acquired = true, capped = true, finite = true;
        foreach (PuppetPartBody part in lab.Buddy.Rig.Parts)
        {
            Vector2 start = part.GlobalPosition;
            Vector2 startViewport = GetViewport().GetCanvasTransform() * start;
            var press = new InputEventMouseButton { ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true, Position = startViewport, GlobalPosition = startViewport };
            Input.ParseInputEvent(press);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            acquired &= lab.Grab.CurrentGrab.Active && lab.Pointer.LastPickedPart == part.PartId;
            Vector2 end = new(Mathf.Clamp(start.X + 100, 60, 420), Mathf.Clamp(start.Y - 50, 60, 300));
            Vector2 endViewport = GetViewport().GetCanvasTransform() * end;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = endViewport, GlobalPosition = endViewport, Relative = endViewport - startViewport, Velocity = (endViewport - startViewport) * 20 });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, ButtonMask = 0, Pressed = false, Position = endViewport, GlobalPosition = endViewport });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            capped &= lab.Grab.LastReleaseSpeed <= lab.Grab.Profile.ThrowSpeedCap + 0.01f;
            for (int tick = 0; tick < 720 && !lab.Buddy.Standing.Snapshot.IsStable; tick++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            finite &= lab.Buddy.Rig.AllBodiesFinite();
        }
        float maxStrain = 0; foreach (LinkTelemetry link in lab.Buddy.Constraints.Telemetry) maxStrain = Mathf.Max(maxStrain, link.Strain);
        state["all_parts_grabbed"] = lab.Pointer.SuccessfulPickCount == 6;
        state["pointer_input_received"] = lab.Pointer.ReceivedInputCount >= 18;
        state["release_velocity_capped"] = capped;
        EnvelopeBoundsProfile bounds = GD.Load<EnvelopeBoundsProfile>("res://data/buddy/lab_envelope_bounds.tres");
        state["rig_connected_after_throws"] = maxStrain <= bounds.MaximumLinkStrain;
        state["standing_after_throws"] = lab.Buddy.Standing.Snapshot.IsStable;
        state["finite_after_throws"] = finite;
    }

    private async System.Threading.Tasks.Task ExerciseWalkJumpAsync(Dictionary<string, bool> state, BuddyLab lab, int timeoutTicks)
    {
        // The shipped profile has ambient jumping OFF (owner decision 2026-07-20), but the
        // jump ACTUATION path is live code that tool reactions and M4 behaviours drive, so
        // this journey keeps exercising takeoff/landing end-to-end through a journey-local
        // profile that differs from the shipped one ONLY by the flag. The shipped datum is
        // asserted separately, exactly as `autonomous_motion` does.
        AutonomousMotionProfile shipped = lab.Buddy.AutonomousMotion.Profile;
        state["shipped_profile_disables_ambient_jumping"] = !shipped.AmbientJumpsEnabled;
        lab.Buddy.AutonomousMotion.Profile = new AutonomousMotionProfile
        {
            ResourceName = "JourneyAmbientJumpsEnabled",
            MinimumIdleTicks = shipped.MinimumIdleTicks,
            MaximumIdleTicks = shipped.MaximumIdleTicks,
            MinimumWalkTicks = shipped.MinimumWalkTicks,
            MaximumWalkTicks = shipped.MaximumWalkTicks,
            MinimumJumpIntervalTicks = shipped.MinimumJumpIntervalTicks,
            MaximumJumpIntervalTicks = shipped.MaximumJumpIntervalTicks,
            IdleWeight = shipped.IdleWeight,
            WalkLeftWeight = shipped.WalkLeftWeight,
            WalkRightWeight = shipped.WalkRightWeight,
            AmbientJumpsEnabled = true,
        };
        lab.Controls.Reseed(lab.Controls.AutonomySeed);

        AutonomousMotionProfile profile = lab.Buddy.AutonomousMotion.Profile;
        int derivedBudget = 8 * (profile.MaximumIdleTicks + profile.MaximumWalkTicks) +
                            2 * profile.MaximumJumpIntervalTicks;
        int ticks = Math.Min(derivedBudget, timeoutTicks);
        bool left = false, right = false, jumped = false, finite = true;
        for (int tick = 0; tick < ticks; tick++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            float direction = lab.Buddy.AutonomousMotion.Intent.WalkDirection;
            left |= direction < 0; right |= direction > 0;
            jumped |= lab.Buddy.AutonomousMotion.JumpRequestCount > 0;
            finite &= lab.Buddy.Rig.AllBodiesFinite();
            if (left && right && jumped && lab.Buddy.Standing.Snapshot.IsStable) break;
        }
        state["walked_left"] = left; state["walked_right"] = right; state["jumped"] = jumped;
        state["landed_standing"] = jumped && lab.Buddy.Standing.Snapshot.IsStable;
        state["motion_contained"] = finite && lab.Buddy.Recovery.AllBodiesInsideSafeBounds();
    }

    private async System.Threading.Tasks.Task ExerciseIdleSoakAsync(Dictionary<string, bool> state, BuddyLab lab, int ticks)
    {
        if (!string.IsNullOrEmpty(_args.ArtifactsDir)) lab.EnableTelemetry(_args.ArtifactsDir, _args.JourneyId ?? "idle_soak");
        SoakProbeResult result = await SoakProbe.RunAsync(GetTree(), lab, ticks);
        lab.TelemetryRecorder?.Complete();
        EnvelopeBoundsProfile bounds = GD.Load<EnvelopeBoundsProfile>("res://data/buddy/lab_envelope_bounds.tres");
        state["soak_finite"] = result.Finite; state["soak_awake"] = result.Awake;
        state["soak_connected"] = result.MaximumStrain <= bounds.MaximumLinkStrain;
        state["soak_contained"] = result.Contained;
        state["soak_envelope_written"] = lab.TelemetryRecorder is not null && System.IO.File.Exists(lab.TelemetryRecorder.EnvelopePath);
    }

    private async System.Threading.Tasks.Task ExerciseM3GloveStrikeAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        long balanceBeforeSelection = lab.Pipeline.BalanceMilliCredits;
        long scoredBeforeSelection = lab.Pipeline.ScoredImpactCount;
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.B, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Key.B, Pressed = false });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        for (int tick = 0; tick < 12; tick++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        state["glove_selected"] = lab.Pipeline.SelectedTool == DesktopBuddy.Domain.Tools.ToolId.BoxingGlove;
        state["tool_activation_does_not_pay"] =
            lab.Pipeline.BalanceMilliCredits == balanceBeforeSelection &&
            lab.Pipeline.ScoredImpactCount == scoredBeforeSelection;

        Vector2 previous = new(32.0f, lab.Buddy.Rig.Head.GlobalPosition.Y);
        await MovePointerAsync(previous, Vector2.Zero);
        for (int tick = 0; tick < 120; tick++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        for (int pass = 0; pass < 6 && lab.Pipeline.ScoredImpactCount == scoredBeforeSelection; pass++)
        {
            float x = pass % 2 == 0 ? 448.0f : 32.0f;
            Vector2 to = new(x, lab.Buddy.Rig.Head.GlobalPosition.Y);
            await MovePointerAsync(to, to - previous);
            previous = to;
            for (int tick = 0; tick < 90 && lab.Pipeline.ScoredImpactCount == scoredBeforeSelection; tick++)
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        for (int tick = 0; tick < 40 && !lab.MoneyHud.RewardLabel.Visible; tick++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        state["glove_strike_paid"] =
            lab.Pipeline.ScoredImpactCount > scoredBeforeSelection &&
            lab.Pipeline.BalanceMilliCredits > balanceBeforeSelection;
        state["money_hud_updated"] = lab.MoneyHud.BalanceLabel.Text == "$" + lab.Pipeline.BalanceCredits;
        state["reward_feedback_visible"] = lab.MoneyHud.RewardLabel.Visible;
        Log.Info("Journey", $"M3 glove raw={lab.Pipeline.RawContactCount} accepted={lab.Pipeline.AcceptedEpisodeCount} " +
            $"scored={lab.Pipeline.ScoredImpactCount} maxImpulse={lab.Pipeline.MaxRawImpulse:F1} " +
            $"active={lab.CursorTools.IsActive} position={lab.CursorTools.Body?.GlobalPosition}");

        async System.Threading.Tasks.Task MovePointerAsync(Vector2 world, Vector2 relative)
        {
            Vector2 viewport = GetViewport().GetCanvasTransform() * world;
            Input.ParseInputEvent(new InputEventMouseMotion
            {
                Position = viewport,
                GlobalPosition = viewport,
                Relative = relative,
                Velocity = relative * 120.0f,
            });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async System.Threading.Tasks.Task ExerciseM3ToolFeelAsync(
        Dictionary<string, bool> state,
        BuddyLab lab)
    {
        await PressToolKeyAsync(Key.F);
        state["pet_selected"] = lab.Pipeline.SelectedTool == DesktopBuddy.Domain.Tools.ToolId.Pet;
        int careBefore = (int)lab.Pipeline.CareAwardCount;
        Vector2 pointer = lab.Buddy.Rig.Head.GlobalPosition;
        await MovePointerAsync(pointer, Vector2.Zero, false);
        await SetPrimaryAsync(pointer, true);
        bool petHandVisible = false;
        bool petFaceSeen = false;
        bool pet3DFaceComposed = false;
        for (int tick = 0; tick < 380 && lab.Pipeline.CareAwardCount == careBefore; tick++)
        {
            Vector2 next = lab.Buddy.Rig.Head.GlobalPosition + Vector2.Right * (tick % 2 == 0 ? -8.0f : 8.0f);
            await MovePointerAsync(next, next - pointer, true);
            pointer = next;
            petHandVisible |= lab.CareCursor.IsHandVisible;
            petFaceSeen |= lab.Reactions.CurrentFace == ":3";
            // Task 5 replaced the upright-glyph check: the composed face plate has no
            // counter-rotation to verify, so the 3D check is now semantic parity — the
            // compositor's last composed state is the pet-rub pose while ":3" shows.
            pet3DFaceComposed |=
                lab.Reactions.CurrentFace == ":3" &&
                lab.Face.LastComposedState.Mouth == FaceMouthPose.CatSmile &&
                lab.Face.LastComposedState.Eyes == FaceEyePose.HappyArc;
        }
        state["pet_hand_visible"] = petHandVisible;
        state["pet_rub_face_seen"] = petFaceSeen;
        state["pet_3d_face_composed"] = pet3DFaceComposed;
        state["pet_rewarded"] = lab.Pipeline.CareAwardCount == careBefore + 1;
        state["pet_completion_smile"] = lab.Reactions.CurrentFace == ":)";
        await SetPrimaryAsync(pointer, false);

        await PressToolKeyAsync(Key.T);
        state["tickle_selected"] = lab.Pipeline.SelectedTool == DesktopBuddy.Domain.Tools.ToolId.Tickle;
        pointer = lab.Buddy.Rig.Head.GlobalPosition;
        await MovePointerAsync(pointer, Vector2.Zero, false);
        await SetPrimaryAsync(pointer, true);
        bool fled = false;
        int tickleStartAwards = (int)lab.Pipeline.CareAwardCount;
        for (int tick = 0; tick < 740 && lab.Pipeline.TickleDisposition != DesktopBuddy.Domain.Mood.TickleDisposition.Angry; tick++)
        {
            Vector2 next = lab.Buddy.Rig.Head.GlobalPosition;
            await MovePointerAsync(next, next - pointer, true);
            pointer = next;
            fled |= lab.ToolReactions.IsTickleFleeing;
        }
        fled |= lab.ToolReactions.IsTickleFleeing;
        state["tickle_two_friendly_rewards"] = lab.Pipeline.CareAwardCount == tickleStartAwards + 2;
        state["tickle_became_angry"] = lab.Pipeline.TickleDisposition == DesktopBuddy.Domain.Mood.TickleDisposition.Angry &&
                                         lab.Reactions.CurrentFace == ">:(";
        state["tickle_fled"] = fled;
        await SetPrimaryAsync(pointer, false);
        for (int tick = 0; tick < 8 * Engine.PhysicsTicksPerSecond + 2; tick++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        state["tickle_cooldown_reset"] = lab.Pipeline.TickleDisposition == DesktopBuddy.Domain.Mood.TickleDisposition.Friendly;

        await ExerciseM3GloveStrikeAsync(state, lab);
        // The deliberately forceful strike may knock the buddy out. Despawn the
        // glove, let the fixed four-second window end, then approach afresh so
        // the conscious learned-defense behavior is what this journey observes.
        await PressToolKeyAsync(Key.F);
        for (int tick = 0;
             tick < 600 && lab.Buddy.CurrentConsciousness != DesktopBuddy.Domain.Buddy.Consciousness.Conscious;
             tick++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        pointer = new Vector2(60.0f, 60.0f);
        await MovePointerAsync(pointer, Vector2.Zero, false);
        await PressToolKeyAsync(Key.B);
        Vector2 protectedCenter = (lab.Buddy.Rig.Head.GlobalPosition + lab.Buddy.Rig.Torso.GlobalPosition) * 0.5f;
        await MovePointerAsync(protectedCenter, protectedCenter - pointer, false);
        bool defended = false;
        for (int tick = 0; tick < 90; tick++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            defended |= lab.ToolReactions.IsDefending && lab.Buddy.CurrentDriveIntent.GuardActive;
        }
        state["glove_defense_raised"] = lab.Pipeline.IsToolHarmful(DesktopBuddy.Domain.Tools.ToolId.BoxingGlove) && defended;

        async System.Threading.Tasks.Task PressToolKeyAsync(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = true });
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        async System.Threading.Tasks.Task MovePointerAsync(Vector2 world, Vector2 relative, bool held)
        {
            Vector2 viewport = GetViewport().GetCanvasTransform() * world;
            Input.ParseInputEvent(new InputEventMouseMotion
            {
                ButtonMask = held ? MouseButtonMask.Left : 0,
                Position = viewport,
                GlobalPosition = viewport,
                Relative = relative,
                Velocity = relative * 120.0f,
            });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }

        async System.Threading.Tasks.Task SetPrimaryAsync(Vector2 world, bool pressed)
        {
            Vector2 viewport = GetViewport().GetCanvasTransform() * world;
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                ButtonMask = pressed ? MouseButtonMask.Left : 0,
                Pressed = pressed,
                Position = viewport,
                GlobalPosition = viewport,
            });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async System.Threading.Tasks.Task ExecuteStepsAsync(
        Dictionary<string, bool> state, BuddyLab lab, JsonElement steps)
    {
        Vector2 lastViewport = Vector2.Zero;
        state["step_known"] = true;
        state["anchor_known"] = true;
        foreach (JsonElement item in steps.EnumerateArray())
        {
            string step = item.TryGetProperty("step", out JsonElement kind) ? kind.GetString() ?? "" : "";
            if (step is "pointer_press" or "drag")
            {
                if (!TryResolveAnchor(lab, item, out Vector2 world))
                {
                    state["anchor_known"] = false;
                    GD.PushError($"Journey anchor_known=false target={item.GetProperty("target").GetString()}");
                    return;
                }
                Vector2 viewport = GetViewport().GetCanvasTransform() * world;
                if (step == "pointer_press")
                    Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true, Position = viewport, GlobalPosition = viewport });
                else
                    Input.ParseInputEvent(new InputEventMouseMotion { Position = viewport, GlobalPosition = viewport, Relative = viewport - lastViewport, Velocity = (viewport - lastViewport) * 120.0f });
                lastViewport = viewport;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
            else if (step == "pointer_release")
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = lastViewport, GlobalPosition = lastViewport });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
            else if (step == "press_key" && item.TryGetProperty("key", out JsonElement key) && key.TryGetInt64(out long keyCode))
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = (Key)keyCode, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = (Key)keyCode, Pressed = false });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
            else if (step == "advance_time" && item.TryGetProperty("ticks", out JsonElement ticksElement) && ticksElement.TryGetInt32(out int ticks) && ticks >= 0)
            {
                for (int tick = 0; tick < ticks; tick++)
                    await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            }
            else
            {
                state["step_known"] = false;
                GD.PushError($"Journey step_known=false step={step}");
                return;
            }
        }
    }

    private static bool TryResolveAnchor(BuddyLab lab, JsonElement step, out Vector2 world)
    {
        world = Vector2.Zero;
        string target = step.TryGetProperty("target", out JsonElement targetElement) ? targetElement.GetString() ?? "" : "";
        if (target.StartsWith("buddy:", StringComparison.Ordinal) &&
            Enum.TryParse(target[6..], out BuddyPartId partId) && Enum.IsDefined(partId))
        {
            world = lab.Buddy.Rig.GetPart(partId).GlobalPosition;
            return true;
        }
        if (target == "sandbox" && step.TryGetProperty("x", out JsonElement x) && x.TryGetSingle(out float px) &&
            step.TryGetProperty("y", out JsonElement y) && y.TryGetSingle(out float py))
        {
            world = new Vector2(px, py);
            return true;
        }
        return false;
    }

    private void Fail(string id, ulong seed, Stopwatch stopwatch, string check, string detail, int exitCode)
    {
        Log.Error("Journey", $"Journey '{id}' failed: {check} ({detail}).");
        VerdictWriter.Write("journey", id, seed, false,
            new[] { new StartupCheck(check, false, detail) },
            new[] { "journey setup failure" }, stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
        QuitSafely(exitCode);
    }

    private void QuitSafely(int exitCode)
    {
        GodotInteropShutdown.PrepareForQuit();
        GetTree().Quit(exitCode);
    }
}

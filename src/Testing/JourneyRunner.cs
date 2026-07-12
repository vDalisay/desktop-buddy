using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Laboratory;
using DesktopBuddy.Platform;
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

            Dictionary<string, bool> state = await ComputeStateAsync(root, seed);

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

            if (root.TryGetProperty("assertions", out JsonElement assertions) &&
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
            VerdictWriter.Write("journey", id, seed, passed, checks, new[] { $"seed={seed}" },
                stopwatch.ElapsedMilliseconds, _args.ArtifactsDir);
            Log.Info("Journey", $"Journey '{id}' {(passed ? "PASSED" : "FAILED")}.");
            GetTree().Quit(passed ? 0 : 1);
        }
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

        // The Work-Mode hit region is the box projected into client pixels; at the
        // default 480x360 room and 100% zoom that is the inner box (16,16,448,328).
        IReadOnlyList<Rect2I> regions = shell.LastWorkModeHitRegions;
        state["hit_region_is_client_box"] =
            regions.Count == 1 && regions[0] == new Rect2I(16, 16, 448, 328);

        await ToggleAsync();
        state["toggle_enters_play"] = shell.Mode == DomainInputMode.Play;

        await EscapeAsync();
        state["escape_returns_to_work"] = shell.Mode == DomainInputMode.Work;

        Rect2 box = sandbox.Boundaries.InnerBounds;
        await ClickWorldAsync(box.GetCenter());
        state["click_inside_enters_play"] = shell.Mode == DomainInputMode.Play;

        await ClickWorldAsync(new Vector2(box.Position.X - 8.0f, box.Position.Y - 8.0f));
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
        var profile = lab.Buddy.AutonomousMotion.Profile;
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
        GetTree().Quit(exitCode);
    }
}

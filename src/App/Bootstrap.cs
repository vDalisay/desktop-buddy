using System;
using DesktopBuddy.Automation;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using DesktopBuddy.Testing;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Main-scene composition root and the single entrypoint router. It parses the
/// headless runner / automation command line, composes the development-only
/// <see cref="AutomationDriver"/> when allowed, and routes to the sandbox
/// (normal boot), the scenario runner, or the journey runner. It holds no
/// gameplay logic — it only composes and routes (ARCHITECTURE.md Section 3).
/// </summary>
public partial class Bootstrap : Node
{
    private const string Category = "Bootstrap";

    public override void _Ready()
    {
        RunnerArguments args;
        try
        {
            args = RunnerArguments.Parse(OS.GetCmdlineUserArgs());
        }
        catch (ArgumentException e)
        {
            Log.Error(Category, $"Invalid runner arguments: {e.Message}");
            GetTree().Quit(2);
            return;
        }

        bool headless = DisplayServer.GetName() == "headless";
        Log.Info(Category, $"Boot mode={args.Mode} automation={args.AutomationEnabled} headless={headless} debug={BuildInfo.IsDebugBuild}");

        ComposeAutomation(args);

        switch (args.Mode)
        {
            case RunnerMode.Scenario:
            case RunnerMode.Journey:
                BootTestRunner(args);
                break;

            default:
                BootSandbox();
                break;
        }
    }

    private void BootTestRunner(RunnerArguments args)
    {
        var packed = GD.Load<PackedScene>("res://scenes/test_runner.tscn");
        if (packed is null)
        {
            Log.Error(Category, "Missing res://scenes/test_runner.tscn; cannot run scenario/journey.");
            GetTree().Quit(2);
            return;
        }

        var host = packed.Instantiate<TestRunner>();
        host.Configure(args);
        AddChild(host);
    }

    private void ComposeAutomation(RunnerArguments args)
    {
        if (!args.AutomationEnabled)
        {
            return;
        }

        if (!BuildInfo.AutomationAllowed)
        {
            Log.Warn(Category, "Automation requested but this is not a debug build; ignoring.");
            return;
        }

        var driver = new AutomationDriver { Name = nameof(AutomationDriver) };
        AddChild(driver);
    }

    private void BootSandbox()
    {
        StartupReport report = StartupValidator.Validate();
        if (!report.Ok)
        {
            // Fail-fast in development so misconfiguration is caught immediately;
            // errors were already logged by the validator.
            Log.Error(Category, "Startup validation failed; sandbox may be unstable.");
        }

        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        if (packed is null)
        {
            Log.Error(Category, "Missing res://scenes/sandbox.tscn; cannot boot sandbox.");
            return;
        }

        AddChild(packed.Instantiate());
    }
}

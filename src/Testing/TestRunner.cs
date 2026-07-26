using DesktopBuddy.App;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.Domain.Automation;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>
/// Thin composition root of <c>res://scenes/test_runner.tscn</c>: the host scene
/// for headless scenario and journey runs. Bootstrap instantiates it (rather
/// than a normal sandbox) when a runner mode is on the command line, configures
/// it, and lets it spawn the matching runner. Keeping this a scene gives the
/// test entrypoints a real, inspectable composition root and keeps runner
/// selection out of Bootstrap.
/// </summary>
public partial class TestRunner : Node
{
    private RunnerArguments _args = new();

    public void Configure(RunnerArguments args) => _args = args;

    public override void _Ready()
    {
        switch (_args.Mode)
        {
            case RunnerMode.Scenario:
                var scenarioRunner = new ScenarioRunner { Name = nameof(ScenarioRunner) };
                scenarioRunner.Configure(_args);
                AddChild(scenarioRunner);
                break;

            case RunnerMode.Journey:
                var journeyRunner = new JourneyRunner { Name = nameof(JourneyRunner) };
                journeyRunner.Configure(_args);
                AddChild(journeyRunner);
                break;

            default:
                Log.Warn("TestRunner", "TestRunner started without a scenario/journey mode; nothing to run.");
                GodotInteropShutdown.PrepareForQuit();
                GetTree().Quit(0);
                break;
        }
    }
}

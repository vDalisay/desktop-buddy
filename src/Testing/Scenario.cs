using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>Outcome of one headless scenario run.</summary>
public sealed record ScenarioResult(
    bool Passed,
    IReadOnlyList<StartupCheck> Checks,
    IReadOnlyList<string> Messages);

/// <summary>
/// A seeded headless Godot scenario (TEST_PLAN.md Section 3). Scenarios exercise
/// rigid-body behavior, spring constraints, tools, containment, and scene wiring
/// and assert ranges/tolerances rather than bit-exact transforms. They are
/// invoked through the <c>--scenario=&lt;id&gt; --seed=&lt;n&gt;</c> runner protocol
/// and never ship in release exports.
/// </summary>
public interface IScenario
{
    /// <summary>Stable scenario id used on the command line.</summary>
    string Id { get; }

    /// <summary>Run the scenario against the live tree with the injected seed.</summary>
    Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed);
}

using System.Collections.Generic;
using System.Linq;

namespace DesktopBuddy.App;

/// <summary>One named startup check and its outcome.</summary>
public sealed record StartupCheck(string Name, bool Passed, string Detail);

/// <summary>
/// Aggregated result of <see cref="StartupValidator"/>. The boot smoke
/// scenario/journey asserts <see cref="Ok"/> is true (ROADMAP.md Milestone 0
/// exit criteria, AGENT_VERIFICATION_AND_E2E.md Section 7).
/// </summary>
public sealed class StartupReport
{
    private readonly List<StartupCheck> _checks = new();

    public IReadOnlyList<StartupCheck> Checks => _checks;

    public bool Ok => _checks.All(c => c.Passed);

    public IEnumerable<StartupCheck> Failures => _checks.Where(c => !c.Passed);

    public void Add(string name, bool passed, string detail) =>
        _checks.Add(new StartupCheck(name, passed, detail));

    /// <summary>Record a pass/fail with a comparison detail in one call.</summary>
    public void Expect(string name, bool passed, string detail) => Add(name, passed, detail);
}

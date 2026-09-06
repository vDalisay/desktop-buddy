using System.Runtime.CompilerServices;

namespace DesktopBuddy.Testing;

/// <summary>Registers expressive-presentation regression coverage without growing the legacy registry.</summary>
internal static class ExpressivePresentationScenarioRegistration
{
    [ModuleInitializer]
    internal static void Register() =>
        PhaseACharacterScenarioCatalog.Register(
            "expressive_presentation",
            static () => new ExpressivePresentationScenario());
}

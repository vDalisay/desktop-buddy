using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DesktopBuddy.Testing;

internal static class EnvironmentScenarioRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        FieldInfo field = typeof(ScenarioCatalog).GetField("Factories", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Scenario registry field was not found.");
        var factories = (Dictionary<string, Func<IScenario>>?)field.GetValue(null)
            ?? throw new InvalidOperationException("Scenario registry was not initialized.");
        factories["environment_trusted_definitions"] = () => new EnvironmentTrustedDefinitionsScenario();
        factories["environment_background_editor"] = () => new EnvironmentBackgroundEditorScenario();
        factories["environment_startup_registration"] = () => new EnvironmentStartupRegistrationScenario();
    }
}

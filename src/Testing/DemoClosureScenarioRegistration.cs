using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DesktopBuddy.Testing;

/// <summary>Registers Demo-closure scenarios without expanding the legacy central registry.</summary>
internal static class DemoClosureScenarioRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        FieldInfo field = typeof(ScenarioCatalog).GetField(
            "Factories",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Scenario registry field was not found.");
        var factories = (Dictionary<string, Func<IScenario>>?)field.GetValue(null)
            ?? throw new InvalidOperationException("Scenario registry was not initialized.");
        factories["demo_meal_visual"] = () => new DemoMealVisualScenario();
        factories["demo_care_tool_presentation"] = () => new DemoCareToolPresentationScenario();
        factories["tutorial_closure"] = () => new TutorialClosureScenario();
    }
}
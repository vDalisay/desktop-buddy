using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DesktopBuddy.Testing;

/// <summary>
/// Phase A test-only registration extension. It appends to the existing stable registry
/// without expanding the legacy catalogue source for every focused editor task.
/// </summary>
internal static class CharacterEditorScenarioRegistration
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
        factories["character_editor_state_machine"] = () => new CharacterEditorStateMachineScenario();
        factories["character_editor_randomization"] = () => new CharacterEditorRandomizationScenario();
        factories["editor_preview_has_no_physics"] = () => new EditorPreviewHasNoPhysicsScenario();
    }
}

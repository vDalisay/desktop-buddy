using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class CharacterEditorCreateUseAndReactJourney : IJourney
{
    public string Id => "character_editor_create_use_and_react";

    public async Task<JourneyResult> RunAsync(SceneTree tree, ulong seed)
    {
        ScenarioResult result = await new CharacterEditorPhaseAExitScenario()
            .RunAsync(tree, seed);
        return new JourneyResult(result.Passed, result.Checks, result.Messages);
    }
}

internal static class CharacterEditorJourneyRegistration
{
    [ModuleInitializer]
    internal static void Register()
    {
        FieldInfo field = typeof(JourneyCatalog).GetField(
            "Factories",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Journey registry field was not found.");
        var factories = (Dictionary<string, Func<IJourney>>?)field.GetValue(null)
            ?? throw new InvalidOperationException("Journey registry was not initialized.");
        factories["character_editor_create_use_and_react"] =
            () => new CharacterEditorCreateUseAndReactJourney();
    }
}

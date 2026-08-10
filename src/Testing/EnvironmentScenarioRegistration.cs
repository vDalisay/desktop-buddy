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

        // Keep the established scenario IDs stable, but route them through the ED6 closure
        // implementations so the runner no longer asserts the original six-item/Buy-button slice.
        factories["environment_trusted_definitions"] = () => new EnvironmentTrustedDefinitionsClosureScenario();
        factories["environment_background_editor"] = () => new EnvironmentBackgroundEditorClosureScenario();
        factories["environment_startup_registration"] = () => new EnvironmentStartupClosureScenario();
        factories["environment_placement_engine"] = () => new EnvironmentPlacementClosureScenario();
        factories["environment_decorator"] = () => new EnvironmentDecoratorClosureScenario();

        // Named gates from ENVIRONMENT_DECORATOR_IMPLEMENTATION_PLAN §15. Several intentionally
        // share one matrix: the matrix exercises the coupled invariants together rather than
        // letting purchase/cancel/wallpaper semantics drift in independent fixtures.
        factories["environment_decor_catalogue"] = () =>
            new EnvironmentTrustedDefinitionsClosureScenario("environment_decor_catalogue");
        factories["environment_decor_purchase_per_instance"] = () =>
            new EnvironmentTransactionClosureScenario("environment_decor_purchase_per_instance");
        factories["environment_decor_cancel_transaction"] = () =>
            new EnvironmentTransactionClosureScenario("environment_decor_cancel_transaction");
        factories["environment_decor_free_placement"] = () =>
            new EnvironmentPlacementClosureScenario("environment_decor_free_placement");
        factories["environment_decor_grid_snap"] = () =>
            new EnvironmentPlacementClosureScenario("environment_decor_grid_snap");
        factories["environment_decor_rotation"] = () =>
            new EnvironmentTransactionClosureScenario("environment_decor_rotation");
        factories["environment_decor_resize_mapping"] = () =>
            new EnvironmentPlacementClosureScenario("environment_decor_resize_mapping");
        factories["environment_decor_wallpaper_slot"] = () =>
            new EnvironmentTransactionClosureScenario("environment_decor_wallpaper_slot");
        factories["environment_decor_input_ownership"] = () =>
            new EnvironmentDecoratorClosureScenario("environment_decor_input_ownership");
        factories["environment_decor_restart_restore"] = () =>
            new EnvironmentRestartRestoreClosureScenario();
        factories["environment_decorator_room_build"] = () =>
            new EnvironmentDecoratorClosureScenario("environment_decorator_room_build");
    }
}

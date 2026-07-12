using System.Collections.Generic;
using DesktopBuddy.Diagnostics;
using Godot;

namespace DesktopBuddy.App;

/// <summary>
/// Fail-fast startup validation of the invariants a shipped build depends on:
/// the fixed physics tick, the transparency-allowed flag, the declared input
/// actions, and the named collision layers. Any provided <see cref="GameResource"/>
/// definitions are validated too. Returns a structured <see cref="StartupReport"/>
/// rather than throwing, so the boot smoke runner can emit a machine-readable
/// verdict either way (ARCHITECTURE.md Section 16, ROADMAP.md Milestone 0).
/// </summary>
public static class StartupValidator
{
    private const string Category = "Startup";

    private static readonly (string Setting, string Expected)[] ExpectedLayerNames =
    {
        ("layer_names/2d_physics/layer_1", "RoomBounds"),
        ("layer_names/2d_physics/layer_2", "BuddyParts"),
        ("layer_names/2d_physics/layer_3", "LooseObjects"),
        ("layer_names/2d_physics/layer_4", "Projectiles"),
        ("layer_names/2d_physics/layer_5", "PhysicalTools"),
        ("layer_names/2d_physics/layer_6", "InteractionSense"),
    };

    /// <param name="resources">
    /// Loaded typed content resources to validate. Empty in Milestone 0; later
    /// milestones pass the active <c>BuddyDefinition</c>, tool definitions, etc.
    /// </param>
    public static StartupReport Validate(IReadOnlyList<GameResource>? resources = null)
    {
        var report = new StartupReport();

        int physicsHz = (int)ProjectSettings.GetSetting("physics/common/physics_ticks_per_second", 0);
        report.Expect("physics_tick_120hz", physicsHz == 120, $"physics_ticks_per_second={physicsHz}");

        int maxSteps = (int)ProjectSettings.GetSetting("physics/common/max_physics_steps_per_frame", 0);
        report.Expect("max_physics_steps_bounded", maxSteps > 0, $"max_physics_steps_per_frame={maxSteps}");

        bool interpolation = (bool)ProjectSettings.GetSetting("physics/common/physics_interpolation", false);
        report.Expect("physics_interpolation_on", interpolation, $"physics_interpolation={interpolation}");

        bool perPixelAllowed = (bool)ProjectSettings.GetSetting("display/window/per_pixel_transparency/allowed", false);
        report.Expect("per_pixel_transparency_allowed", perPixelAllowed, $"allowed={perPixelAllowed}");

        // Window baseline the desktop shell depends on (DECISIONS.md "Overlay and
        // Interface"; ARCHITECTURE.md §20). These are the transparent borderless
        // topmost box defaults; a build that silently flips one loses the shell.
        bool borderless = (bool)ProjectSettings.GetSetting("display/window/size/borderless", false);
        report.Expect("window_borderless", borderless, $"borderless={borderless}");

        bool alwaysOnTop = (bool)ProjectSettings.GetSetting("display/window/size/always_on_top", false);
        report.Expect("window_always_on_top", alwaysOnTop, $"always_on_top={alwaysOnTop}");

        bool transparent = (bool)ProjectSettings.GetSetting("display/window/size/transparent", false);
        report.Expect("window_transparent", transparent, $"transparent={transparent}");

        int viewportWidth = (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 0);
        int viewportHeight = (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 0);
        report.Expect("window_default_size_480x360", viewportWidth == 480 && viewportHeight == 360,
            $"viewport={viewportWidth}x{viewportHeight}");

        var stretch = ProjectSettings.GetSetting("display/window/stretch/mode", "").AsString();
        report.Expect("stretch_disabled", stretch == "disabled", $"stretch/mode='{stretch}'");

        var userDir = ProjectSettings.GetSetting("application/config/custom_user_dir_name", "").AsString();
        report.Expect("custom_user_dir", userDir == "DesktopBuddy", $"custom_user_dir_name='{userDir}'");

        foreach (string action in InputActions.All)
        {
            bool has = InputMap.HasAction(action);
            report.Expect($"input_action:{action}", has, has ? "present" : "missing");
        }

        foreach ((string setting, string expected) in ExpectedLayerNames)
        {
            var actual = ProjectSettings.GetSetting(setting, "").AsString();
            report.Expect($"layer:{expected}", actual == expected, $"{setting}='{actual}'");
        }

        if (resources is not null)
        {
            foreach (GameResource resource in resources)
            {
                Godot.Collections.Array<string> errors = resource.Validate();
                string name = string.IsNullOrEmpty(resource.ResourceName) ? resource.GetType().Name : resource.ResourceName;
                report.Expect($"resource:{name}", errors.Count == 0, errors.Count == 0 ? "valid" : string.Join("; ", errors));
            }
        }

        LogOutcome(report);
        return report;
    }

    private static void LogOutcome(StartupReport report)
    {
        if (report.Ok)
        {
            Log.Info(Category, $"Startup validation passed ({report.Checks.Count} checks).");
            return;
        }

        foreach (StartupCheck failure in report.Failures)
        {
            Log.Error(Category, $"Startup check failed: {failure.Name} ({failure.Detail}).");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DesktopBuddy.Objects;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.Testing;

/// <summary>DEMO-8 gate for replacing the Meal's generic flat-circle presentation.</summary>
public sealed class DemoMealVisualScenario : IScenario
{
    public string Id => "demo_meal_visual";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        LooseObjectProfile? profile = ResourceLoader.Load<LooseObjectProfile>("res://data/objects/meal.tres");
        bool authored = GodotObject.IsInstanceValid(profile) &&
            profile!.Visual3D == LooseObjectVisualKind.Meal &&
            profile.IsRuntimeValid;
        checks.Add(new StartupCheck(
            "demo_meal_authors_recognizable_3d_kind",
            authored,
            $"loaded={GodotObject.IsInstanceValid(profile)} kind={profile?.Visual3D}"));

        if (!authored)
            return Task.FromResult(new ScenarioResult(false, checks, [$"seed={seed}"]));

        ArrayMesh mesh = MealMeshBuilder.PlatedSandwich(
            profile!.Radius,
            profile.FillColor,
            profile.OutlineColor);
        Vector3[] faces = mesh.GetFaces();
        float maximum = faces.Length == 0 ? float.PositiveInfinity : faces.Max(vertex => vertex.Length());
        checks.Add(new StartupCheck(
            "demo_meal_mesh_is_nonempty_and_bounded",
            faces.Length == 144 && maximum <= profile.Radius * MealMeshBuilder.EnvelopeRadiusFactor,
            $"vertices={faces.Length} max={maximum:F2} bound={profile.Radius * MealMeshBuilder.EnvelopeRadiusFactor:F2}"));

        bool colorLayers = false;
        if (mesh.GetSurfaceCount() == 1)
        {
            Godot.Collections.Array arrays = mesh.SurfaceGetArrays(0);
            Color[] colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
            colorLayers = colors.Length == faces.Length &&
                colors.Length > 0 && colors.Any(color => color != colors[0]);
        }
        checks.Add(new StartupCheck(
            "demo_meal_mesh_keeps_distinct_food_layers",
            colorLayers,
            "plate/bread/filling use vertex-colour layers"));

        bool passed = checks.All(check => check.Passed);
        return Task.FromResult(new ScenarioResult(passed, checks, [$"seed={seed}"]));
    }
}

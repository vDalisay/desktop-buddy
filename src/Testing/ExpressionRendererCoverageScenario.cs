using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class ExpressionRendererCoverageScenario : IScenario
{
    public string Id => "expression_renderer_coverage";

    public Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        var registry = new CharacterFeatureRendererRegistry();
        CharacterFeatureCatalog catalog = CharacterFeatureCatalog.Shipped;

        checks.Add(SetCheck("a3_eye_registry_exact", catalog.GetIds(CharacterFeatureSlot.Eyes), registry.EyeIds));
        checks.Add(SetCheck("a3_brow_registry_exact", catalog.GetIds(CharacterFeatureSlot.Brows), registry.BrowIds));
        checks.Add(SetCheck("a3_mouth_registry_exact", catalog.GetIds(CharacterFeatureSlot.Mouth), registry.MouthIds));
        checks.Add(SetCheck("a3_accent_registry_exact", catalog.GetIds(CharacterFeatureSlot.TorsoAccent), registry.AccentIds));

        Color outline = new(0.08f, 0.06f, 0.10f);
        bool eyesComplete = true;
        foreach (string id in registry.EyeIds)
        {
            CompiledFeatureAppearance appearance = Feature(id);
            foreach (FaceEyePose pose in Enum.GetValues<FaceEyePose>())
            {
                eyesComplete &= registry.Eyes(id).Build(appearance, pose, false, new Vector2(0.5f, -0.5f), outline) is not null;
                eyesComplete &= registry.Eyes(id).Build(appearance, pose, true, Vector2.Zero, outline) is not null;
            }
        }
        checks.Add(new StartupCheck("a3_all_eye_poses_render", eyesComplete, $"renderers={registry.EyeIds.Count}"));

        bool browsComplete = true;
        foreach (string id in registry.BrowIds)
        {
            CompiledFeatureAppearance appearance = Feature(id);
            foreach (FaceBrowPose pose in Enum.GetValues<FaceBrowPose>())
                browsComplete &= registry.Brows(id).Build(appearance, pose, outline) is not null;
        }
        checks.Add(new StartupCheck("a3_all_brow_poses_render", browsComplete, $"renderers={registry.BrowIds.Count}"));

        bool mouthsComplete = true;
        foreach (string id in registry.MouthIds)
        {
            CompiledFeatureAppearance appearance = Feature(id);
            foreach (FaceMouthPose pose in Enum.GetValues<FaceMouthPose>())
                mouthsComplete &= registry.Mouth(id).Build(appearance, pose, outline) is not null;
        }
        checks.Add(new StartupCheck("a3_all_mouth_poses_render", mouthsComplete, $"renderers={registry.MouthIds.Count}"));

        bool semanticCoverage = true;
        foreach (string face in FaceExpressionCatalog.Faces)
        {
            FaceFeaturePose pose = FaceExpressionCatalog.Resolve(face);
            foreach (string eyeId in registry.EyeIds)
                semanticCoverage &= registry.Eyes(eyeId).Build(Feature(eyeId), pose.Eyes, false, Vector2.Zero, outline) is not null;
            foreach (string browId in registry.BrowIds)
                semanticCoverage &= registry.Brows(browId).Build(Feature(browId), pose.Brows, outline) is not null;
            foreach (string mouthId in registry.MouthIds)
                semanticCoverage &= registry.Mouth(mouthId).Build(Feature(mouthId), pose.Mouth, outline) is not null;
        }
        checks.Add(new StartupCheck("a3_semantic_faces_cover_all_renderers", semanticCoverage,
            $"faces={FaceExpressionCatalog.Faces.Count}"));

        IReadOnlyList<CharacterDrawCommand> none = registry.Accent(CharacterFeatureIds.AccentNone)
            .Build(Feature(CharacterFeatureIds.AccentNone), outline);
        checks.Add(new StartupCheck("a3_accent_none_empty", none.Count == 0, $"commands={none.Count}"));

        Vector2 identity = CharacterFeatureTransform.Apply(new Vector2(0.2f, -0.4f), NormalizedFeatureTransform.Identity);
        Vector2 minimum = CharacterFeatureTransform.Apply(Vector2.Zero,
            new NormalizedFeatureTransform(-1.0, -1.0, NormalizedFeatureTransform.MinimumScale));
        Vector2 maximum = CharacterFeatureTransform.Apply(Vector2.Zero,
            new NormalizedFeatureTransform(1.0, 1.0, NormalizedFeatureTransform.MaximumScale));
        bool transformsCorrect = identity.IsEqualApprox(new Vector2(0.2f, -0.4f)) &&
            minimum.IsEqualApprox(new Vector2(-CharacterFeatureTransform.OffsetExtent, -CharacterFeatureTransform.OffsetExtent)) &&
            maximum.IsEqualApprox(new Vector2(CharacterFeatureTransform.OffsetExtent, CharacterFeatureTransform.OffsetExtent));
        checks.Add(new StartupCheck("a3_transform_bounds", transformsCorrect,
            $"identity={identity} minimum={minimum} maximum={maximum}"));

        Type[] rendererTypes = typeof(CharacterFeatureRendererRegistry).Assembly.GetTypes()
            .Where(static type => type.Namespace == "DesktopBuddy.Buddy.Presentation3D.Characters")
            .ToArray();
        bool noPhysicsReferences = rendererTypes.All(static type =>
            !ReferencesPhysics(type));
        checks.Add(new StartupCheck("a3_renderers_have_no_physics_contract", noPhysicsReferences,
            $"types={rendererTypes.Length}"));

        bool passed = checks.All(static check => check.Passed);
        return Task.FromResult(new ScenarioResult(passed, checks, messages));
    }

    private static CompiledFeatureAppearance Feature(string id) => new(
        id,
        NormalizedFeatureTransform.Identity,
        new Rgba32(92, 180, 230));

    private static StartupCheck SetCheck(
        string id,
        IEnumerable<string> expected,
        IEnumerable<string> actual)
    {
        string[] left = expected.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        string[] right = actual.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        bool equal = left.SequenceEqual(right, StringComparer.Ordinal);
        return new StartupCheck(id, equal,
            $"expected=[{string.Join(",", left)}] actual=[{string.Join(",", right)}]");
    }

    private static bool ReferencesPhysics(Type type)
    {
        static bool Physics(Type? candidate) =>
            candidate?.Namespace?.Contains(".Physics", StringComparison.Ordinal) == true;

        if (Physics(type.BaseType))
            return true;
        if (type.GetInterfaces().Any(Physics))
            return true;
        if (type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(field => Physics(field.FieldType)))
            return true;
        if (type.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(property => Physics(property.PropertyType)))
            return true;
        return type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(method => Physics(method.ReturnType) || method.GetParameters().Any(parameter => Physics(parameter.ParameterType)));
    }
}

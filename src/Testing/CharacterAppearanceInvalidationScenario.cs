using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Testing;

public sealed class CharacterAppearanceInvalidationScenario : IScenario
{
    public string Id => "character_appearance_invalidation";

    public async Task<ScenarioResult> RunAsync(SceneTree tree, ulong seed)
    {
        var checks = new List<StartupCheck>();
        var messages = new List<string> { $"seed={seed}" };
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
            return new ScenarioResult(false, [new StartupCheck("a4_scene_loadable", false, "buddy_lab")], messages);

        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        BuddyVisualRigView rig = lab.VisualPresenter.RigView;
        rig.RefreshCharacterCompositors();

        long face0 = rig.CharacterFaceRenderCount;
        long accent0 = rig.CharacterAccentRenderCount;
        long material0 = rig.PartMaterialMutationCount;
        CompiledCharacterAppearance builtIn = BuiltInCharacterAppearance.Value;
        CompiledCharacterAppearance colorOnly = builtIn with
        {
            CharacterId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            PartColors = builtIn.PartColors with { Head = new Rgba32(210, 80, 90) },
        };
        rig.ApplyAppearance(colorOnly);
        rig.RefreshCharacterCompositors();
        checks.Add(new StartupCheck(
            "a4_base_color_does_not_repaint_decals",
            rig.CharacterFaceRenderCount == face0 &&
            rig.CharacterAccentRenderCount == accent0 &&
            rig.PartMaterialMutationCount > material0,
            $"face={face0}->{rig.CharacterFaceRenderCount} accent={accent0}->{rig.CharacterAccentRenderCount} " +
            $"materials={material0}->{rig.PartMaterialMutationCount}"));

        CompiledCharacterAppearance eyeChanged = colorOnly with
        {
            CharacterId = Guid.Parse("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"),
            Eyes = colorOnly.Eyes with { ResolvedFeatureId = CharacterFeatureIds.EyesRoundDot },
        };
        long faceBeforeEye = rig.CharacterFaceRenderCount;
        long accentBeforeEye = rig.CharacterAccentRenderCount;
        rig.ApplyAppearance(eyeChanged);
        rig.RefreshCharacterCompositors();
        checks.Add(new StartupCheck(
            "a4_eye_change_exactly_one_face_render",
            rig.CharacterFaceRenderCount == faceBeforeEye + 1 &&
            rig.CharacterAccentRenderCount == accentBeforeEye,
            $"face={faceBeforeEye}->{rig.CharacterFaceRenderCount} " +
            $"accent={accentBeforeEye}->{rig.CharacterAccentRenderCount}"));

        long equalFace = rig.CharacterFaceRenderCount;
        long equalAccent = rig.CharacterAccentRenderCount;
        rig.ApplyAppearance(eyeChanged);
        rig.RefreshCharacterCompositors();
        checks.Add(new StartupCheck(
            "a4_equal_key_no_render",
            rig.CharacterFaceRenderCount == equalFace &&
            rig.CharacterAccentRenderCount == equalAccent,
            $"face={equalFace}->{rig.CharacterFaceRenderCount} accent={equalAccent}->{rig.CharacterAccentRenderCount}"));

        FaceRenderState pain = FaceComposer.Compose(
            FaceExpressionCatalog.Resolve(">_<"), false, false, 0, true, 0.0f, 0.0f);
        long semanticFace = rig.CharacterFaceRenderCount;
        long semanticAccent = rig.CharacterAccentRenderCount;
        rig.SetPreviewFaceState(pain);
        checks.Add(new StartupCheck(
            "a4_semantic_change_only_face_render",
            rig.CharacterFaceRenderCount == semanticFace + 1 &&
            rig.CharacterAccentRenderCount == semanticAccent &&
            rig.LastCharacterFaceRenderKey?.State == pain,
            $"face={semanticFace}->{rig.CharacterFaceRenderCount} accent={semanticAccent}->{rig.CharacterAccentRenderCount}"));

        CompiledCharacterAppearance accentChanged = eyeChanged with
        {
            CharacterId = Guid.Parse("cccccccc-cccc-4ccc-8ccc-cccccccccccc"),
            TorsoAccent = eyeChanged.TorsoAccent with
            {
                ResolvedFeatureId = CharacterFeatureIds.AccentChevron,
            },
        };
        long faceBeforeAccent = rig.CharacterFaceRenderCount;
        long accentBeforeAccent = rig.CharacterAccentRenderCount;
        rig.ApplyAppearance(accentChanged);
        rig.RefreshCharacterCompositors();
        checks.Add(new StartupCheck(
            "a4_accent_change_exactly_one_accent_render",
            rig.CharacterFaceRenderCount == faceBeforeAccent &&
            rig.CharacterAccentRenderCount == accentBeforeAccent + 1 &&
            rig.TorsoAccentPlate.Visible,
            $"face={faceBeforeAccent}->{rig.CharacterFaceRenderCount} " +
            $"accent={accentBeforeAccent}->{rig.CharacterAccentRenderCount}"));

        bool allSemanticStates = true;
        foreach (string face in FaceExpressionCatalog.Faces)
        {
            FaceFeaturePose pose = FaceExpressionCatalog.Resolve(face);
            FaceRenderState state = FaceComposer.Compose(pose, false, false, 0, false, 0.0f, 0.0f);
            rig.SetPreviewFaceState(state);
            allSemanticStates &= rig.LastCharacterFaceRenderKey?.State == state;
        }
        checks.Add(new StartupCheck("a4_every_semantic_face_updates_key", allSemanticStates,
            $"faces={FaceExpressionCatalog.Faces.Count}"));

        CharacterCompileResult defaultCompile = CharacterCompiler.Compile(
            CharacterDocument.CreateDefault(
                Guid.Parse("dddddddd-dddd-4ddd-8ddd-dddddddddddd"),
                "Default"),
            CharacterFeatureCatalog.Shipped);
        bool defaultParity = defaultCompile.Appearance is { } compiled &&
            compiled.PartColors == builtIn.PartColors &&
            compiled.Eyes == builtIn.Eyes &&
            compiled.Brows == builtIn.Brows &&
            compiled.Mouth == builtIn.Mouth &&
            compiled.TorsoAccent == builtIn.TorsoAccent;
        checks.Add(new StartupCheck("a4_default_document_builtin_parity", defaultParity,
            $"compiled={defaultCompile.IsSuccess}"));

        bool layerOrder = rig.FacePlate is { } facePlate &&
            facePlate.GetParent() == rig.GetPartSocket(BuddyPartId.Head) &&
            facePlate.GetIndex() > rig.GetPartMesh(BuddyPartId.Head).GetIndex() &&
            rig.TorsoAccentPlate.GetParent() == rig.GetPartSocket(BuddyPartId.Torso) &&
            rig.TorsoAccentPlate.GetIndex() > rig.GetPartMesh(BuddyPartId.Torso).GetIndex();
        checks.Add(new StartupCheck("a4_decal_layer_above_surface", layerOrder,
            $"face_plate={rig.FacePlate is not null} accent_plate={rig.TorsoAccentPlate is not null}"));

        rig.ClearPreviewFaceState();
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        bool passed = true;
        foreach (StartupCheck check in checks)
            passed &= check.Passed;
        return new ScenarioResult(passed, checks, messages);
    }
}

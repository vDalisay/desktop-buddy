using System;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

public readonly record struct FaceRenderKey(
    CompiledFeatureAppearance Eyes,
    CompiledFeatureAppearance Brows,
    CompiledFeatureAppearance Mouth,
    FaceRenderState State,
    Color TrustedOutlineColor);

public readonly record struct AccentRenderKey(
    CompiledFeatureAppearance Accent,
    Color TrustedOutlineColor);

public static class BuiltInCharacterAppearance
{
    private static readonly Rgba32 FeatureColor = Rgba32.Parse("#2A1A12");

    public static CompiledCharacterAppearance Value { get; } = new(
        Guid.Empty,
        new PartColorSet(
            CharacterPartColors.BuiltInHead,
            CharacterPartColors.BuiltInTorso,
            CharacterPartColors.BuiltInHand,
            CharacterPartColors.BuiltInHand,
            CharacterPartColors.BuiltInFoot,
            CharacterPartColors.BuiltInFoot),
        new CompiledFeatureAppearance(
            CharacterFeatureIds.EyesButton,
            NormalizedFeatureTransform.Identity,
            FeatureColor),
        new CompiledFeatureAppearance(
            CharacterFeatureIds.BrowsSoftArc,
            NormalizedFeatureTransform.Identity,
            FeatureColor),
        new CompiledFeatureAppearance(
            CharacterFeatureIds.MouthRounded,
            NormalizedFeatureTransform.Identity,
            FeatureColor),
        new CompiledFeatureAppearance(
            CharacterFeatureIds.AccentNone,
            NormalizedFeatureTransform.Identity,
            FeatureColor));

    public static FaceRenderState NeutralFaceState { get; } = FaceComposer.Compose(
        FaceExpressionCatalog.Resolve(":|"),
        blinkClosed: false,
        chewActive: false,
        chewFrame: 0,
        faceSuppressed: false,
        pupilX: 0.0f,
        pupilY: 0.0f);
}

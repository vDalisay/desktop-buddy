using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

/// <summary>
/// Fabric-doll button eyes. The discs stay physically button-like across expressions while
/// brows and mouth retain the semantic expression range. Pupil tracking is intentionally
/// ignored because the X-shaped thread is fixed to each button.
/// </summary>
internal sealed class ButtonEyeRenderer : ICharacterEyeRenderer
{
    private static readonly Color ThreadColor = new("E0C59B");

    public string FeatureId => CharacterFeatureIds.EyesButton;

    public IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        FaceEyePose pose,
        bool blinking,
        Vector2 pupilOffset,
        Color trustedOutlineColor)
    {
        _ = pupilOffset;

        var commands = new List<CharacterDrawCommand>(10);
        Color buttonColor = CharacterFeatureColors.ToGodot(appearance.Color);
        NormalizedFeatureTransform transform = appearance.Transform;
        float radius = pose switch
        {
            FaceEyePose.Wide => 0.155f,
            FaceEyePose.Narrow => 0.12f,
            _ => 0.14f,
        };
        float threadHalfSpan = blinking ? 0.05f : 0.065f;

        AddButton(commands, new Vector2(-0.34f, 0.16f), radius, threadHalfSpan,
            buttonColor, trustedOutlineColor, transform);
        AddButton(commands, new Vector2(0.34f, 0.16f), radius, threadHalfSpan,
            buttonColor, trustedOutlineColor, transform);
        return commands;
    }

    private static void AddButton(
        List<CharacterDrawCommand> commands,
        Vector2 center,
        float radius,
        float threadHalfSpan,
        Color buttonColor,
        Color outline,
        in NormalizedFeatureTransform transform)
    {
        ProceduralEyeRenderer.AddCircle(
            commands,
            center,
            radius,
            buttonColor,
            outline,
            transform,
            outlineExpansion: 0.025f);

        AddThread(commands,
            [
                center + new Vector2(-threadHalfSpan, threadHalfSpan),
                center + new Vector2(threadHalfSpan, -threadHalfSpan),
            ],
            transform);
        AddThread(commands,
            [
                center + new Vector2(-threadHalfSpan, -threadHalfSpan),
                center + new Vector2(threadHalfSpan, threadHalfSpan),
            ],
            transform);
    }

    private static void AddThread(
        List<CharacterDrawCommand> commands,
        Vector2[] points,
        in NormalizedFeatureTransform transform)
    {
        commands.Add(CharacterDrawCommand.Stroke(
            CharacterFeatureTransform.Apply(points, transform),
            CharacterFeatureTransform.ApplyLength(0.026f, transform),
            ThreadColor));
    }
}

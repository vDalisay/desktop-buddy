using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

public interface ICharacterFeatureRenderer
{
    string FeatureId { get; }
}

public interface ICharacterEyeRenderer : ICharacterFeatureRenderer
{
    IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        FaceEyePose pose,
        bool blinking,
        Vector2 pupilOffset,
        Color trustedOutlineColor);
}

public interface ICharacterBrowRenderer : ICharacterFeatureRenderer
{
    IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        FaceBrowPose pose,
        Color trustedOutlineColor);
}

public interface ICharacterMouthRenderer : ICharacterFeatureRenderer
{
    IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        FaceMouthPose pose,
        Color trustedOutlineColor);
}

public interface ICharacterAccentRenderer : ICharacterFeatureRenderer
{
    IReadOnlyList<CharacterDrawCommand> Build(
        in CompiledFeatureAppearance appearance,
        Color trustedOutlineColor);
}

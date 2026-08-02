using System;
using System.Collections.Generic;
using DesktopBuddy.Domain.Characters;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D.Characters;

/// <summary>
/// Parameterized face decal compositor. Equality of <see cref="FaceRenderKey"/> is the
/// sole invalidation rule; headless mode executes the same key and counter path without a
/// viewport or GPU readback.
/// </summary>
public sealed class ParametricFaceCompositor
{
    public const int TextureSize = 200;
    public const float PlateWorldSize = 40.0f;

    private readonly CharacterFeatureRendererRegistry _registry;
    private readonly Color _outline;
    private CompiledCharacterAppearance _appearance = BuiltInCharacterAppearance.Value;
    private FaceRenderState _state = BuiltInCharacterAppearance.NeutralFaceState;
    private bool _initialized;
    private bool _hasRendered;
    private SubViewport? _viewport;
    private CharacterFeaturePainterControl? _painter;

    public ParametricFaceCompositor(
        CharacterFeatureRendererRegistry registry,
        Color trustedOutlineColor)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _outline = trustedOutlineColor;
    }

    public FaceRenderKey? LastRenderKey { get; private set; }
    public long RenderCount { get; private set; }
    public Texture2D? OutputTexture => _viewport?.GetTexture();

    public void Initialize(Node owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_initialized)
            return;

        if (DisplayServer.GetName() != "headless")
        {
            _viewport = new SubViewport
            {
                Name = "ParametricFaceViewport",
                Size = new Vector2I(TextureSize, TextureSize),
                TransparentBg = true,
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            _painter = new CharacterFeaturePainterControl
            {
                Name = "ParametricFacePainter",
                Size = new Vector2(TextureSize, TextureSize),
            };
            _viewport.AddChild(_painter);
            owner.AddChild(_viewport);
        }

        _initialized = true;
        Refresh();
    }

    public void SetAppearance(in CompiledCharacterAppearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        _appearance = appearance;
        Refresh();
    }

    public void SetState(in FaceRenderState state)
    {
        _state = state;
        Refresh();
    }

    public void Refresh()
    {
        if (!_initialized)
            return;

        var key = new FaceRenderKey(
            _appearance.Eyes,
            _appearance.Brows,
            _appearance.Mouth,
            _state,
            _outline);
        if (_hasRendered && LastRenderKey == key)
            return;

        _hasRendered = true;
        LastRenderKey = key;
        RenderCount++;

        if (_painter is null || _viewport is null)
            return;

        var commands = new List<CharacterDrawCommand>(32);
        commands.AddRange(_registry.Eyes(key.Eyes.ResolvedFeatureId).Build(
            key.Eyes,
            key.State.Eyes,
            key.State.Blinking,
            new Vector2(key.State.PupilX, key.State.PupilY),
            key.TrustedOutlineColor));
        commands.AddRange(_registry.Brows(key.Brows.ResolvedFeatureId).Build(
            key.Brows,
            key.State.Brows,
            key.TrustedOutlineColor));
        commands.AddRange(_registry.Mouth(key.Mouth.ResolvedFeatureId).Build(
            key.Mouth,
            key.State.Mouth,
            key.TrustedOutlineColor));
        _painter.Commands = commands;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
}

/// <summary>On-change compositor for the single trusted torso-front accent plate.</summary>
public sealed class BodyAccentCompositor
{
    public const int TextureSize = 256;

    private readonly CharacterFeatureRendererRegistry _registry;
    private readonly Color _outline;
    private CompiledFeatureAppearance _appearance = BuiltInCharacterAppearance.Value.TorsoAccent;
    private bool _initialized;
    private bool _hasRendered;
    private SubViewport? _viewport;
    private CharacterFeaturePainterControl? _painter;

    public BodyAccentCompositor(
        CharacterFeatureRendererRegistry registry,
        Color trustedOutlineColor)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _outline = trustedOutlineColor;
    }

    public AccentRenderKey? LastRenderKey { get; private set; }
    public long RenderCount { get; private set; }
    public Texture2D? OutputTexture => _viewport?.GetTexture();
    public bool HasVisibleAccent =>
        !string.Equals(_appearance.ResolvedFeatureId, CharacterFeatureIds.AccentNone,
            StringComparison.Ordinal);

    public void Initialize(Node owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_initialized)
            return;

        if (DisplayServer.GetName() != "headless")
        {
            _viewport = new SubViewport
            {
                Name = "BodyAccentViewport",
                Size = new Vector2I(TextureSize, TextureSize),
                TransparentBg = true,
                Disable3D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
            };
            _painter = new CharacterFeaturePainterControl
            {
                Name = "BodyAccentPainter",
                Size = new Vector2(TextureSize, TextureSize),
            };
            _viewport.AddChild(_painter);
            owner.AddChild(_viewport);
        }

        _initialized = true;
        Refresh();
    }

    public void SetAppearance(in CompiledFeatureAppearance appearance)
    {
        _appearance = appearance;
        Refresh();
    }

    public void Refresh()
    {
        if (!_initialized)
            return;

        var key = new AccentRenderKey(_appearance, _outline);
        if (_hasRendered && LastRenderKey == key)
            return;

        _hasRendered = true;
        LastRenderKey = key;
        RenderCount++;

        if (_painter is null || _viewport is null)
            return;

        _painter.Commands = _registry.Accent(key.Accent.ResolvedFeatureId)
            .Build(key.Accent, key.TrustedOutlineColor);
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
    }
}

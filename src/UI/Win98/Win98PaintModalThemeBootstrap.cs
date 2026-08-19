using System;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>Applies the shared Win98 theme to modal panels that live beside the themed editor root.</summary>
public partial class Win98PaintModalThemeBootstrap : Node
{
    private const double ResolveIntervalSeconds = 0.20;

    private Theme? _theme;
    private PanelContainer? _newCharacterPrompt;
    private PanelContainer? _unsavedPrompt;
    private double _resolveRemaining;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _resolveRemaining -= Math.Max(0.0, delta);
        if (_resolveRemaining > 0.0)
            return;
        _resolveRemaining = ResolveIntervalSeconds;

        _theme ??= Win98ThemeFactory.Create();

        if (!GodotObject.IsInstanceValid(_newCharacterPrompt))
        {
            _newCharacterPrompt = GetTree().Root.FindChild(
                "Win98NewCharacterPrompt", recursive: true, owned: false) as PanelContainer;
            if (GodotObject.IsInstanceValid(_newCharacterPrompt))
                _newCharacterPrompt!.Theme = _theme;
        }

        if (!GodotObject.IsInstanceValid(_unsavedPrompt))
        {
            _unsavedPrompt = GetTree().Root.FindChild(
                "UnsavedChangesPrompt", recursive: true, owned: false) as PanelContainer;
            if (GodotObject.IsInstanceValid(_unsavedPrompt))
                _unsavedPrompt!.Theme = _theme;
        }

        // Both panels are part of the persistent CharacterEditorHost composition. Once they
        // exist and share the theme there is no dynamic state left for this bootstrap to poll.
        if (GodotObject.IsInstanceValid(_newCharacterPrompt) && GodotObject.IsInstanceValid(_unsavedPrompt))
            SetProcess(false);
    }
}
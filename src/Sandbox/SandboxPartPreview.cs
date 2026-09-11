using DesktopBuddy.Domain.Sandbox;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// A sunken well showing what is about to be placed, in 3D: a part as the room renders it, or a
/// rope, hinge, weld or wire doing its job between real part models (owner note 2026-09-11 — every
/// preview is the 3D thing, not a drawing).
/// </summary>
public partial class SandboxPartPreview : Control
{
    private SandboxModelStage? _stage;

    public override void _Ready()
    {
        var container = new SubViewportContainer
        {
            Name = "PreviewStage",
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.OffsetLeft = container.OffsetTop = 2.0f;
        container.OffsetRight = container.OffsetBottom = -2.0f;
        AddChild(container);

        _stage = new SandboxModelStage(0.08f, 3.0f) { Name = "PreviewViewport", RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        container.AddChild(_stage);
        VisibilityChanged += () =>
        {
            if (_stage is not null)
                _stage.RenderTargetUpdateMode = IsVisibleInTree() ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        };
    }

    public void Show(SandboxPartDefinition? definition) =>
        _stage?.Show(definition is null ? null : SandboxPaletteModels.ForPart(definition));

    public void ShowLink(SandboxLinkKind kind, float strength, float elasticity, float stiffness) =>
        _stage?.Show(SandboxPaletteModels.ForLink(kind, strength, elasticity, stiffness));

    public void ShowWire(SandboxWireColor color) => _stage?.Show(SandboxPaletteModels.ForWire(color));

    private StyleBox? _well;

    public override void _Draw() =>
        DrawStyleBox(_well ??= Win98ThemeFactory.Recessed(Colors.White, 2), new Rect2(Vector2.Zero, Size));
}

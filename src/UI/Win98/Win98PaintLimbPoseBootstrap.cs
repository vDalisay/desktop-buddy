using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// User-testing Show limbs option. It reveals the connector visuals and spreads the hand/foot
/// paint targets in the preview. PaintCanvasControl applies the same offsets to its trusted mapper,
/// so the visible target and painted surface stay aligned at every preview yaw.
/// </summary>
public partial class Win98PaintLimbPoseBootstrap : Node
{
    private readonly Dictionary<BuddyPartId, Vector3> _homePositions = [];
    private CharacterEditorHost? _host;
    private PaintCanvasControl? _canvas;
    private CheckBox? _toggle;

    public override void _Ready() => ProcessMode = ProcessModeEnum.Always;

    public override void _Process(double delta)
    {
        _host ??= GetTree().Root.FindChild(nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        _canvas ??= GetTree().Root.FindChild("CharacterPaintCanvas", true, false) as PaintCanvasControl;
        if (!GodotObject.IsInstanceValid(_host) || !GodotObject.IsInstanceValid(_canvas))
            return;

        EnsureToggle();
        bool active = _canvas!.IsVisibleInTree();
        if (!active)
        {
            if (_canvas.ExpandedLimbPose)
                SetEnabled(false, leavingPaintEditor: true);
            if (GodotObject.IsInstanceValid(_toggle))
                _toggle!.SetPressedNoSignal(false);
            return;
        }

        if (_canvas.ExpandedLimbPose)
            ApplyPreviewPose();
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_canvas) && _canvas!.ExpandedLimbPose)
            SetEnabled(false, leavingPaintEditor: true);
    }

    private void EnsureToggle()
    {
        if (GodotObject.IsInstanceValid(_toggle))
            return;
        if (GetTree().Root.FindChild("PaintMappingOptions", true, false) is not HBoxContainer options)
            return;

        _toggle = options.FindChild("PaintShowLimbsToggle", false, false) as CheckBox;
        if (!GodotObject.IsInstanceValid(_toggle))
        {
            _toggle = new CheckBox
            {
                Name = "PaintShowLimbsToggle",
                Text = "Show limbs",
                TooltipText = "Spread the hands and feet and show connectors so limb paint targets are easier to reach.",
                FocusMode = Control.FocusModeEnum.All,
            };
            _toggle.Toggled += enabled => SetEnabled(enabled, leavingPaintEditor: false);
            options.AddChild(_toggle);
        }
    }

    private void SetEnabled(bool enabled, bool leavingPaintEditor)
    {
        if (!GodotObject.IsInstanceValid(_canvas) || !GodotObject.IsInstanceValid(_host))
            return;

        _canvas!.SetExpandedLimbPose(enabled);
        if (!GodotObject.IsInstanceValid(_host!.PreviewRig) || !_host.PreviewRig.IsInitialized)
            return;

        if (enabled)
        {
            CaptureHomePositions();
            _host.PreviewRig.SetConnectorVisualsVisible(true);
            ApplyPreviewPose();
            return;
        }

        RestoreHomePositions();
        _host.PreviewRig.SetConnectorVisualsVisible(leavingPaintEditor);
        _homePositions.Clear();
    }

    private void CaptureHomePositions()
    {
        if (_homePositions.Count > 0)
            return;
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
        {
            BuddyPartId buddyPart = ToBuddyPart(part);
            _homePositions[buddyPart] = _host!.PreviewRig.GetPartSocket(buddyPart).Position;
        }
    }

    private void ApplyPreviewPose()
    {
        if (_homePositions.Count == 0 || !GodotObject.IsInstanceValid(_host?.PreviewRig))
            return;

        foreach (PaintPart part in Enum.GetValues<PaintPart>())
        {
            BuddyPartId buddyPart = ToBuddyPart(part);
            if (!_homePositions.TryGetValue(buddyPart, out Vector3 home))
                continue;
            PaintPoint offset = PaintCanvasControl.LimbPoseOffsetFor(part);
            _host!.PreviewRig.GetPartSocket(buddyPart).Position =
                home + new Vector3((float)offset.X, (float)-offset.Y, 0.0f);
        }
    }

    private void RestoreHomePositions()
    {
        if (!GodotObject.IsInstanceValid(_host?.PreviewRig))
            return;
        foreach ((BuddyPartId part, Vector3 position) in _homePositions)
            _host!.PreviewRig.GetPartSocket(part).Position = position;
    }

    private static BuddyPartId ToBuddyPart(PaintPart part) => part switch
    {
        PaintPart.Head => BuddyPartId.Head,
        PaintPart.Torso => BuddyPartId.Torso,
        PaintPart.LeftHand => BuddyPartId.LeftHand,
        PaintPart.RightHand => BuddyPartId.RightHand,
        PaintPart.LeftFoot => BuddyPartId.LeftFoot,
        PaintPart.RightFoot => BuddyPartId.RightFoot,
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown paint part."),
    };
}

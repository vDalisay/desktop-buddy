using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.CharacterEditor;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.UI.Win98;

/// <summary>
/// User-testing Show limbs option. It reveals the connector visuals and spreads the head, hand and
/// foot paint targets in the preview. PaintCanvasControl applies the same offsets to its trusted
/// mapper, so the visible targets and painted body surfaces stay aligned at every preview yaw.
/// The neck connector is presentation-only; the persisted connector paint atlas remains hand/foot only.
/// </summary>
public partial class Win98PaintLimbPoseBootstrap : Node
{
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
                TooltipText = "Spread the head, hands and feet and show connectors so the neck and limb targets are easier to inspect.",
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
            _host.PreviewRig.SetConnectorVisualsVisible(true);
            ApplyPreviewPose();
            return;
        }

        ApplyPreviewPose();
        _host.PreviewRig.SetConnectorVisualsVisible(leavingPaintEditor);
    }

    private void ApplyPreviewPose()
    {
        if (!GodotObject.IsInstanceValid(_host?.PreviewRig))
            return;

        BuddyVisualRigView rig = _host!.PreviewRig;
        BuddyVisualPartPose Pose(PaintPart part)
        {
            BuddyPartId buddyPart = ToBuddyPart(part);
            BuddyVisualTransform home = rig.GeometrySource.ReadTransform(buddyPart);
            PaintPoint offset = _canvas?.ExpandedLimbPose == true
                ? PaintCanvasControl.LimbPoseOffsetFor(part)
                : default;
            var position = home.Position + new Vector2((float)offset.X, (float)offset.Y);
            var rendered = home with { Position = position };
            return new BuddyVisualPartPose(
                rendered,
                WorldPlaneMapping.To3D(position),
                new Vector3(0.0f, 0.0f, WorldPlaneMapping.To3DRotationZ(home.Rotation)));
        }

        Vector3 rotation = rig.Rotation;
        rig.Rotation = Vector3.Zero;
        rig.ApplyPose(new BuddyVisualPoseFrame(
            Pose(PaintPart.Head),
            Pose(PaintPart.Torso),
            Pose(PaintPart.LeftHand),
            Pose(PaintPart.RightHand),
            Pose(PaintPart.LeftFoot),
            Pose(PaintPart.RightFoot),
            0.0f,
            BuiltInCharacterAppearance.NeutralFaceState,
            string.Empty,
            0.0f));
        rig.Rotation = rotation;
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
using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Buddy.Presentation3D.Characters;
using DesktopBuddy.Domain.Painting;
using DesktopBuddy.Domain.Presentation;
using DesktopBuddy.Presentation3D;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    /// <summary>
    /// Re-applies the browser Paint Buddy's static editor pose without rotating its artificial
    /// depth lanes into the screen plane. The legacy paint controls keep the rig root yaw as the
    /// mapping signal; each socket is then placed at the same yaw with lane depth added afterwards,
    /// matching BuddyVisualPresenter. This also preserves the optional Show limbs pose.
    /// </summary>
    internal void ReapplyBrowserPaintPreviewPose()
    {
        if (!OperatingSystem.IsBrowser() ||
            !IsEditorOpen ||
            !GodotObject.IsInstanceValid(_preview) ||
            !_preview.IsInitialized ||
            _previewSource is null)
        {
            return;
        }

        float yawRadians = _paintRotationQuarterTurns * Mathf.Pi * 0.5f;
        float yawDegrees = Mathf.RadToDeg(yawRadians);
        _preview.RotationDegrees = new Vector3(0.0f, yawDegrees, 0.0f);

        BuddyVisualTransform torsoHome = _previewSource.ReadTransform(BuddyPartId.Torso);
        Vector2 torsoPosition = torsoHome.Position;

        BuddyVisualPartPose Pose(PaintPart paintPart)
        {
            BuddyPartId part = ToBuddyPart(paintPart);
            BuddyVisualTransform home = _previewSource.ReadTransform(part);
            PaintPoint offset = GodotObject.IsInstanceValid(_paintCanvas) && _paintCanvas.ExpandedLimbPose
                ? PaintCanvasControl.LimbPoseOffsetFor(paintPart)
                : default;
            Vector2 position = home.Position + new Vector2((float)offset.X, (float)offset.Y);
            BuddyVisualTransform rendered = home with { Position = position };
            return new BuddyVisualPartPose(
                rendered,
                _preview.EditorLanePosition(part, position, yawRadians, torsoPosition),
                new Vector3(
                    0.0f,
                    yawRadians,
                    WorldPlaneMapping.To3DRotationZ(home.Rotation)));
        }

        _preview.ApplyPose(new BuddyVisualPoseFrame(
            Pose(PaintPart.Head),
            Pose(PaintPart.Torso),
            Pose(PaintPart.LeftHand),
            Pose(PaintPart.RightHand),
            Pose(PaintPart.LeftFoot),
            Pose(PaintPart.RightFoot),
            yawRadians,
            BuiltInCharacterAppearance.NeutralFaceState,
            string.Empty,
            0.0f));
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

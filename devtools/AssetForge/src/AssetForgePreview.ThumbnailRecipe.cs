using DesktopBuddy.AssetForge.Core;
using Godot;

namespace DesktopBuddy.AssetForge;

public partial class AssetForgePreview
{
    /// <summary>
    /// Applies the authored shared thumbnail camera contract. Export priming calls this after a
    /// generated Buddy asset appears, before the user can make arbitrary orbit/pan state part of a
    /// catalogue thumbnail. Environment thumbnails are CPU-normalized and do not use this camera.
    /// </summary>
    internal void ApplyCanonicalThumbnailPose(ThumbnailSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ResetView();
        SetReferenceVisible(true);
        if (!GodotObject.IsInstanceValid(_orbit) || !GodotObject.IsInstanceValid(_camera)) return;

        _orbit.RotationDegrees = new Vector3(
            (float)settings.PitchDegrees,
            (float)settings.YawDegrees,
            0f);
        float padding = Mathf.Clamp((float)settings.Padding, 0f, .45f);
        _camera.Size *= 1f + (padding * 2f);
    }
}

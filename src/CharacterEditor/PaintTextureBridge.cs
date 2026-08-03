using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

/// <summary>
/// Main-thread-only bridge from CPU-authoritative paint bytes to trusted visual underlays.
/// Dirty revisions are coalesced so each part uploads at most once per rendered frame.
/// </summary>
public sealed class PaintTextureBridge : IDisposable
{
    private readonly BuddyVisualRigView _rig;
    private readonly Dictionary<PaintPart, ImageTexture> _textures = new();
    private readonly Dictionary<PaintPart, long> _uploadedRevisions = new();
    private readonly HashSet<PaintPart> _queued = new();
    private bool _disposed;

    public PaintTextureBridge(BuddyVisualRigView rig)
    {
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));
    }

    public int UploadCount { get; private set; }

    public void Queue(PaintPart part, PaintSurface surface)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surface);
        if (_uploadedRevisions.TryGetValue(part, out long uploaded) && uploaded == surface.Revision)
            return;
        _queued.Add(part);
    }

    public void FlushFrame(IReadOnlyDictionary<PaintPart, PaintSurface> surfaces)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(surfaces);
        if (_queued.Count == 0)
            return;

        foreach (PaintPart part in _queued)
        {
            PaintSurface surface = surfaces[part];
            if (_uploadedRevisions.TryGetValue(part, out long uploaded) && uploaded == surface.Revision)
                continue;

            byte[] pixels = surface.ClonePixels();
            Image image = Image.CreateFromData(
                PaintPolicy.SurfaceSize,
                PaintPolicy.SurfaceSize,
                false,
                Image.Format.Rgba8,
                pixels);

            if (!_textures.TryGetValue(part, out ImageTexture? texture))
            {
                texture = ImageTexture.CreateFromImage(image);
                _textures.Add(part, texture);
                _rig.SetSurfaceUnderlay(ToBuddyPart(part), texture);
            }
            else
            {
                texture.Update(image);
            }

            _uploadedRevisions[part] = surface.Revision;
            UploadCount++;
        }

        _queued.Clear();
    }

    public void Clear()
    {
        if (_disposed)
            return;
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            _rig.SetSurfaceUnderlay(ToBuddyPart(part), null);
        foreach (ImageTexture texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();
        _uploadedRevisions.Clear();
        _queued.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        Clear();
        _disposed = true;
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

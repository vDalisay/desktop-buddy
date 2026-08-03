using System;
using System.Collections.Generic;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Buddy.Presentation3D;
using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>Main-thread runtime binding for already-decoded paint payloads.</summary>
public sealed class RuntimePaintTextureBridge : IDisposable
{
    private readonly BuddyVisualRigView _rig;
    private readonly Dictionary<PaintPart, ImageTexture> _textures = [];
    private readonly Dictionary<PaintPart, byte[]> _pixels = [];

    public RuntimePaintTextureBridge(BuddyVisualRigView rig) =>
        _rig = rig ?? throw new ArgumentNullException(nameof(rig));

    public long UploadCount { get; private set; }

    public void Apply(IReadOnlyDictionary<PaintPart, byte[]> prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
        {
            if (!prepared.TryGetValue(part, out byte[]? bytes))
            {
                ClearPart(part);
                continue;
            }
            if (bytes.Length != PaintPolicy.SurfaceBytes)
                throw new InvalidOperationException($"Prepared paint for {part} is not 512x512 RGBA8.");
            if (_pixels.TryGetValue(part, out byte[]? current) && current.AsSpan().SequenceEqual(bytes))
                continue;

            Image image = Image.CreateFromData(
                PaintPolicy.SurfaceSize,
                PaintPolicy.SurfaceSize,
                false,
                Image.Format.Rgba8,
                bytes);
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
            _pixels[part] = (byte[])bytes.Clone();
            UploadCount++;
        }
    }

    public void Clear()
    {
        foreach (PaintPart part in Enum.GetValues<PaintPart>())
            ClearPart(part);
    }

    public void Dispose()
    {
        Clear();
        foreach (ImageTexture texture in _textures.Values)
            texture.Dispose();
        _textures.Clear();
    }

    private void ClearPart(PaintPart part)
    {
        _pixels.Remove(part);
        if (_textures.Remove(part, out ImageTexture? texture))
        {
            _rig.SetSurfaceUnderlay(ToBuddyPart(part), null);
            texture.Dispose();
        }
    }

    private static BuddyPartId ToBuddyPart(PaintPart part) => part switch
    {
        PaintPart.Head => BuddyPartId.Head,
        PaintPart.Torso => BuddyPartId.Torso,
        PaintPart.LeftHand => BuddyPartId.LeftHand,
        PaintPart.RightHand => BuddyPartId.RightHand,
        PaintPart.LeftFoot => BuddyPartId.LeftFoot,
        PaintPart.RightFoot => BuddyPartId.RightFoot,
        _ => throw new ArgumentOutOfRangeException(nameof(part)),
    };
}

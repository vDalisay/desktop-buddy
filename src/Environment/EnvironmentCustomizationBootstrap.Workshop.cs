using System;
using System.Threading;
using System.Threading.Tasks;
using DesktopBuddy.Domain.Environment;

namespace DesktopBuddy.Environment;

public partial class EnvironmentCustomizationBootstrap : IRoomPaintingSharingHost
{
    /// <summary>
    /// Returns an immutable snapshot for Workshop export. It never exposes the mutable canvas and
    /// falls back to the persisted room when presentation has not been composed yet.
    /// </summary>
    public byte[] SnapshotRoomPaintingForSharing()
    {
        if (Godot.GodotObject.IsInstanceValid(_backgroundPresenter))
            return _backgroundPresenter!.Canvas.ClonePixels();
        return _paintStore?.Load() ?? new byte[EnvironmentCanvasPolicy.Bytes];
    }

    /// <summary>
    /// Explicit apply boundary for an already validated/imported room preset. Workshop import does
    /// not call this method; the player must choose Apply in the Workshop window.
    /// </summary>
    public async Task<bool> ApplySharedRoomPaintingAsync(
        ReadOnlyMemory<byte> pixels,
        CancellationToken token = default)
    {
        if (pixels.Length != EnvironmentCanvasPolicy.Bytes || _paintStore is null)
            return false;
        await _paintStore.SaveAsync(pixels, token);
        if (Godot.GodotObject.IsInstanceValid(_backgroundPresenter))
        {
            _backgroundPresenter!.Canvas.Replace(pixels.Span);
            _backgroundPresenter.Canvas.MarkSaved();
        }
        return true;
    }
}

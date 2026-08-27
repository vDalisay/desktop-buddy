using System;
using System.Threading;
using System.Threading.Tasks;

namespace DesktopBuddy.Environment;

/// <summary>
/// Narrow application boundary exposed to Workshop UI. Sharing can snapshot or explicitly apply a
/// validated room painting without depending on the Environment feature's composition root.
/// </summary>
public interface IRoomPaintingSharingHost
{
    byte[] SnapshotRoomPaintingForSharing();

    Task<bool> ApplySharedRoomPaintingAsync(
        ReadOnlyMemory<byte> pixels,
        CancellationToken token = default);
}

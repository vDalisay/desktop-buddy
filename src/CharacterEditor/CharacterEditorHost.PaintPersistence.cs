using System;
using System.Threading.Tasks;
using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    private bool _paintPersistenceAttached;

    internal async Task AttachPaintSessionAsync(PaintCanvasControl canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_paintPersistenceAttached || !IsInitialized || _context.Characters is null)
            return;

        _paintPersistenceAttached = true;
        var store = new CharacterPaintStore(
            new CharacterFileSystem(),
            _context.Characters.Paths.Root);
        await _session.AttachPaintingAsync(store, canvas.Workspace);
        _session.Changed += QueuePaintAfterSessionChange;
        QueueAllPaintTextures();
    }

    private void QueuePaintAfterSessionChange()
    {
        if (_paintCanvas is not null && _paintTextures is not null)
            QueueAllPaintTextures();
    }
}

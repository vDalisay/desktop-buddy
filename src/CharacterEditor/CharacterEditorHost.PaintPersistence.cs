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
        // Save and Reset read Session.IsDirty, which folds in the paint workspace. Painting
        // raises no session Changed, so without this the first stroke left both buttons
        // disabled and the only way out of the editor was Use Character.
        canvas.Workspace.DirtyChanged += RefreshAll;
        QueueAllPaintTextures();
    }

    private void QueuePaintAfterSessionChange()
    {
        if (_paintCanvas is not null && _paintTextures is not null)
            QueueAllPaintTextures();
    }
}

using System.Threading.Tasks;
using DesktopBuddy.CharacterEditor.BuddyStudio;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    private BuddyStudioWorkspace? _buddyStudio;
    private Control? _legacyCharacterEditor;

    public bool IsBuddyStudioReady => IsInitialized && GodotObject.IsInstanceValid(_buddyStudio);

    public bool EnsureBuddyStudioReady()
    {
        if (!IsInitialized)
            return false;
        if (GodotObject.IsInstanceValid(_buddyStudio))
            return true;

        _legacyCharacterEditor = _editorRoot.GetChildCount() > 0
            ? _editorRoot.GetChild<Control>(0)
            : null;
        var preview = _editorRoot.FindChild("CharacterPreview", true, false) as Control;
        if (!GodotObject.IsInstanceValid(preview))
            return false;

        _buddyStudio = new BuddyStudioWorkspace { Visible = false };
        _buddyStudio.Configure(
            _session,
            _context.Economy,
            preview!,
            CloseBuddyStudioImmediately,
            () => _context.Saves.FlushProgressAsync(force: true));
        _editorRoot.AddChild(_buddyStudio);
        return true;
    }

    public async Task OpenBuddyStudioAsync()
    {
        if (!EnsureBuddyStudioReady())
            return;
        await OpenEditorAsync();
        if (!IsEditorOpen)
            return;
        if (GodotObject.IsInstanceValid(_legacyCharacterEditor))
            _legacyCharacterEditor!.Visible = false;
        _unsavedPanel.Visible = false;
        _buddyStudio!.AttachPreview();
        _buddyStudio!.Visible = true;
    }

    private void CloseBuddyStudioImmediately()
    {
        if (GodotObject.IsInstanceValid(_buddyStudio))
        {
            _buddyStudio!.DetachPreview();
            _buddyStudio!.Visible = false;
        }
        if (GodotObject.IsInstanceValid(_legacyCharacterEditor))
            _legacyCharacterEditor!.Visible = true;
        _unsavedPanel.Visible = false;
        CloseEditorImmediately();
    }
}

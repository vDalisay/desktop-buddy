namespace DesktopBuddy.CharacterEditor;

public partial class CharacterEditorHost
{
    /// <summary>
    /// Read-only presentation policy seam for code-owned capture FX living under editor controls.
    /// The durable setting remains owned/applied by the shell; FX never edits settings itself.
    /// </summary>
    internal bool ReducedParticlesForCaptureFx =>
        IsInitialized && _sandbox.Shell.CurrentLocalSettings.ReducedParticles;
}

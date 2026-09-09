using DesktopBuddy.Persistence.Characters;

namespace DesktopBuddy.App;

public partial class SandboxRoot
{
    /// <summary>
    /// Internal composition seam for runtime systems that must inspect the persisted Character bound
    /// to a Buddy identity. Gameplay actors still receive no arbitrary file-system or RunContext access.
    /// </summary>
    internal CharacterStore? CharacterDocuments => _runContext?.Characters;
}

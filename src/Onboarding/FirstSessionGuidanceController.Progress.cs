using System;
using DesktopBuddy.Domain.Persistence;

namespace DesktopBuddy.Onboarding;

public partial class FirstSessionGuidanceController
{
    /// <summary>
    /// Selects the account-global tutorial progress owner before the controller enters the tree.
    /// Initial Demo remains compatible with the existing Configure overload; Scene-enabled
    /// composition calls this afterwards so onboarding writes the split player document.
    /// </summary>
    public void UsePlayerProgress(PlayerRuntimeProgressBinding progress)
    {
        if (IsInsideTree())
            throw new InvalidOperationException("Tutorial progress must be selected before startup.");
        _tutorial = new TutorialProgressState(
            progress ?? throw new ArgumentNullException(nameof(progress)));
    }
}

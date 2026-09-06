namespace DesktopBuddy.Onboarding;

public partial class FirstSessionGuidanceController
{
    /// <summary>
    /// Bind the Demo helper only when no test/future final-art presenter was injected explicitly.
    /// _EnterTree runs after Configure but before _Ready/RefreshHint, so the existing seam remains
    /// authoritative and test fixtures can still supply their own presenter. The expressive-text
    /// bridge is presentation-only and is installed for every presenter choice.
    /// </summary>
    public override void _EnterTree()
    {
        _characterPresenter ??= new DemoTutorialCharacterPresenter(this);
        InstallExpressiveTextBridge();
    }
}

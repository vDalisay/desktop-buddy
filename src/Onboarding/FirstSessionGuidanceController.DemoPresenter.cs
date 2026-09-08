namespace DesktopBuddy.Onboarding;

public partial class FirstSessionGuidanceController
{
    /// <summary>
    /// Bind the live physics-free Buddy guide only when no test presenter was injected explicitly.
    /// _EnterTree runs after Configure but before _Ready/RefreshHint, so tutorial authority remains
    /// in this controller and tests can still provide their own narrow presenter.
    /// </summary>
    public override void _EnterTree()
    {
        _characterPresenter ??= new LiveTutorialBuddyPresenter(this, _sandbox);
        InstallExpressiveTextBridge();
    }
}

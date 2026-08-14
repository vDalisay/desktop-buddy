using System.Linq;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    internal int ReplacementConnectorCorrectionCountForTest =>
        _replacementConnectorWasCorrected.Count(static corrected => corrected);

    internal bool ReplacementConnectorTrackingReadyForTest =>
        _replacementConnectorWasCorrected.Length == ConnectorVisualCount &&
        _lastReplacementConnectorPosition.Length == ConnectorVisualCount &&
        _lastReplacementConnectorOffset.Length == ConnectorVisualCount;
}

using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class PaintCanvasControl
{
    public PaintCanvasControl()
    {
        Workspace.PreviewTransactionEnded += OnPreviewTransactionEnded;
    }

    private void OnPreviewTransactionEnded()
    {
        if (_curvePhase == BuddyPaintCurvePhase.Idle)
            return;

        ClearCurveState();
        if (_painting)
        {
            _painting = false;
            Input.UseAccumulatedInput = true;
        }
        _sprayPulseAccumulator = 0;
        WorkspaceChanged?.Invoke();
        QueueRedraw();
    }
}

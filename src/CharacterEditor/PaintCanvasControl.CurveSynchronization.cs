using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class PaintCanvasControl
{
    public PaintCanvasControl()
    {
        Workspace.PreviewTransactionEnded += OnPreviewTransactionEnded;
    }

    /// <summary>
    /// One brush-size seam for UI buttons. Curves are a live preview, so their already-visible
    /// rasterisation must immediately adopt the new shared Brush Size instead of waiting for the
    /// next pointer movement.
    /// </summary>
    public void AdjustBrushAndRefreshPreview(int steps)
    {
        Workspace.AdjustBrush(steps);
        RefreshPendingCurvePreview();
        WorkspaceChanged?.Invoke();
        QueueRedraw();
    }

    /// <summary>Re-rasterises a pending curve after paint properties such as selected color change.</summary>
    public void RefreshPendingCurvePreview()
    {
        if (CurvePending && Workspace.PreviewActive)
            RenderCurvePreview();
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

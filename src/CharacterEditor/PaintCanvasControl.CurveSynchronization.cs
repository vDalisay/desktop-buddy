using DesktopBuddy.Domain.Painting;
using Godot;

namespace DesktopBuddy.CharacterEditor;

public partial class PaintCanvasControl
{
    public PaintCanvasControl()
    {
        Workspace.PreviewTransactionEnded += OnPreviewTransactionEnded;
        WorkspaceChanged += KeepPaintInputAccumulated;
    }

    /// <summary>
    /// Buddy paint must keep Godot's mouse-motion accumulation enabled. The canvas historically
    /// disabled accumulation while dragging, which made high-polling mice feed every raw motion
    /// event into the CPU paint rasterizer. At large brush sizes that multiplied expensive stamps
    /// hundreds or thousands of times per second and produced a backlog that looked like striped,
    /// discontinuous paint. The room painter already relies on accumulated input and remains smooth;
    /// Buddy paint uses the same frame-coalesced policy while PaintWorkspace still interpolates
    /// between the coalesced samples for continuous strokes.
    /// </summary>
    private static void KeepPaintInputAccumulated()
    {
        if (!Input.UseAccumulatedInput)
            Input.UseAccumulatedInput = true;
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

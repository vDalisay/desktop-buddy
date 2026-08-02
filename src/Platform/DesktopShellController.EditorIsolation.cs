using Godot;

namespace DesktopBuddy.Platform;

public partial class DesktopShellController
{
    public bool EditorBoundaryIsolationActive { get; private set; }
    public int EditorResizeObservationCount { get; private set; }
    public int RestoredBoundaryRequestCount { get; private set; }

    /// <summary>
    /// Gameplay is paused while editor mode is active, so queued client sizes cannot be
    /// applied. This seam records editor resizes and discards the pending editor size before
    /// the tree resumes.
    /// </summary>
    public void BeginEditorBoundaryIsolation()
    {
        if (EditorBoundaryIsolationActive)
            return;
        EditorBoundaryIsolationActive = true;
        _pendingClientSize = null;
        Window.ClientBoundsChanged += ObserveEditorResize;
    }

    /// <summary>Queues exactly one restored-size layout for the first resumed fixed tick.</summary>
    public void EndEditorBoundaryIsolation(Vector2I restoredClientSize)
    {
        if (!EditorBoundaryIsolationActive)
            return;
        Window.ClientBoundsChanged -= ObserveEditorResize;
        EditorBoundaryIsolationActive = false;
        _pendingClientSize = restoredClientSize;
        RestoredBoundaryRequestCount++;
    }

    private void ObserveEditorResize(Rect2I bounds) => EditorResizeObservationCount++;
}

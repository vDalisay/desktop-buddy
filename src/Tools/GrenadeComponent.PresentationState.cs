using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

public readonly record struct GrenadePresentationState(
    int RuntimeId,
    GrenadeFuseStage Stage,
    int FuseTicksRemaining,
    bool PinIsOut);

public partial class GrenadeComponent
{
    /// <summary>
    /// Read-only presentation seam for the multi-grenade renderer. Fuse state stays private and
    /// authoritative here; presenters can ask how one runtime ID should look but cannot mutate it.
    /// </summary>
    public bool TryGetPresentationState(int runtimeId, out GrenadePresentationState presentation)
    {
        if (runtimeId != 0 && _tracked.TryGetValue(runtimeId, out TrackedGrenadeState? state) &&
            GodotObject.IsInstanceValid(state.Body) && state.Body.RuntimeId == runtimeId)
        {
            presentation = new GrenadePresentationState(
                runtimeId,
                state.Phase.Stage,
                state.Phase.TicksRemaining,
                state.Phase.PinIsOut);
            return true;
        }

        presentation = default;
        return false;
    }
}

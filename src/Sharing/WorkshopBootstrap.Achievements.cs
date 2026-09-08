using Godot;

namespace DesktopBuddy.Sharing;

public partial class WorkshopBootstrap
{
    /// <summary>
    /// The one already-initialized project-owned Steam bridge for sibling platform adapters.
    /// Callers receive no transport internals and cannot initialize a second Steam session.
    /// </summary>
    internal Node? InitializedSteamBridge
    {
        get
        {
            Node? bridge = GetNodeOrNull<Node>("GodotSteamBridge");
            if (!GodotObject.IsInstanceValid(bridge))
                return null;

            try
            {
                return bridge!.Call("is_available").AsBool() ? bridge : null;
            }
            catch
            {
                return null;
            }
        }
    }
}

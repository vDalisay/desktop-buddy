using System;
using Godot;

namespace DesktopBuddy.App;

public static class SceneInstantiation
{
    public static T Instantiate<T>(PackedScene scene) where T : Node
    {
        // Godot 4.6.1 passes only a native pointer into Instantiate. NativeAOT can
        // otherwise finalize the wrapper (and free SceneState) during node construction.
        T instance = scene.Instantiate<T>();
        GC.KeepAlive(scene);
        return instance;
    }
}

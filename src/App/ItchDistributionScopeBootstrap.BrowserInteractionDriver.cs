using System;
using DesktopBuddy.CharacterEditor;
using Godot;

namespace DesktopBuddy.App;

public sealed partial class ItchDistributionScopeBootstrap
{
    /// <summary>
    /// Keep browser interaction maintenance alive while Paint Buddy intentionally hides the
    /// command bar. The normal runtime-ready predicate includes a visible command bar, so once the
    /// editor opens that predicate becomes false by design; without this independent callback the
    /// browser smoke driver, ASCII paint glyph repair, and WASM continuation pump would stop at
    /// exactly the point they are meant to exercise.
    /// </summary>
    public override void _PhysicsProcess(double delta)
    {
        if (!OperatingSystem.IsBrowser() || !_browserRuntimeReadyReported)
            return;

        SandboxRoot? sandbox = GetTree().Root.FindChild("Sandbox", true, false) as SandboxRoot;
        if (!GodotObject.IsInstanceValid(sandbox))
            return;

        CharacterEditorHost? host =
            sandbox!.GetNodeOrNull<CharacterEditorHost>(nameof(CharacterEditorHost));
        if (host is not { IsInitialized: true })
            return;

        EnsureBrowserSynchronizationContext();
        NormalizeBrowserGlyphs(host);
        RunBrowserPaintSmoke(host);
    }
}

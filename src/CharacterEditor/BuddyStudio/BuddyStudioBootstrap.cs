using System;
using System.Linq;
using DesktopBuddy.Diagnostics;
using DesktopBuddy.UI.Win98;
using Godot;

namespace DesktopBuddy.CharacterEditor.BuddyStudio;

/// <summary>
/// Reserved composition root for the Buddy Studio branch. It is intentionally inert on the
/// shared baseline: the branch may compose/register Buddy Studio here without touching
/// project.godot or the shared command-bar bootstrap.
/// </summary>
public partial class BuddyStudioBootstrap : Node
{
    private const string Category = "BuddyStudioStartup";
    private IDisposable? _registration;
    private string _lastWaitReason = string.Empty;
    private bool _startupProbeScheduled;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        Log.Info(Category, $"Autoload ready at {GetPath()}.");
    }

    public override void _Process(double delta)
    {
        if (_registration is not null)
            return;
        CharacterEditorHost? host = GetTree().Root.FindChild(
            nameof(CharacterEditorHost), true, false) as CharacterEditorHost;
        Win98CommandBarBootstrap? commandBar = GetNodeOrNull<Win98CommandBarBootstrap>(
            "/root/Win98CommandBarBootstrap");
        if (!GodotObject.IsInstanceValid(commandBar))
            commandBar = GetTree().Root.FindChild(
                nameof(Win98CommandBarBootstrap), true, false) as Win98CommandBarBootstrap;

        string waitReason = !GodotObject.IsInstanceValid(host)
            ? "waiting for CharacterEditorHost"
            : !GodotObject.IsInstanceValid(commandBar)
                ? "waiting for Win98CommandBarBootstrap"
                : !host!.IsInitialized
                    ? "waiting for CharacterEditorHost initialization"
                    : string.Empty;
        if (waitReason.Length > 0)
        {
            LogWaitReason(waitReason);
            return;
        }
        if (!host!.EnsureBuddyStudioReady())
        {
            LogWaitReason("waiting for Buddy Studio workspace composition");
            return;
        }

        _registration = commandBar!.RegisterTopLevelCommand(
            new TopLevelCommandDefinition(
                TopLevelCommandIds.BuddyStudio,
                "Buddy Studio",
                "Customize your buddy's appearance.",
                TopLevelCommandIds.BuddyStudioOrder),
            () => _ = host.OpenBuddyStudioAsync(),
            isVisible: () => host.IsBuddyStudioReady,
            isEnabled: () => host.IsBuddyStudioReady && !host.IsEditorOpen);
        Log.Info(Category, "Buddy Studio top-level command registered and ready.");
        ScheduleStartupProbeIfRequested();
    }

    public override void _ExitTree()
    {
        _registration?.Dispose();
        _registration = null;
    }

    private void LogWaitReason(string reason)
    {
        if (string.Equals(_lastWaitReason, reason, StringComparison.Ordinal))
            return;
        _lastWaitReason = reason;
        Log.Info(Category, reason + ".");
    }

    private void ScheduleStartupProbeIfRequested()
    {
        const string flag = "--buddy-studio-startup-check";
        bool requested = OS.GetCmdlineArgs().Contains(flag, StringComparer.Ordinal) ||
            OS.GetCmdlineUserArgs().Contains(flag, StringComparer.Ordinal);
        if (_startupProbeScheduled || !OS.IsDebugBuild() || !requested)
            return;
        _startupProbeScheduled = true;
        Callable.From(RunStartupProbe).CallDeferred();
    }

    private async void RunStartupProbe()
    {
        bool passed = await BuddyStudioStartupProbe.RunAsync(GetTree());
        GetTree().Quit(passed ? 0 : 1);
    }
}

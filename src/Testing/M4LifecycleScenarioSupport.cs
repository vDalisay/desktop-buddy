using System;
using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Content;
using DesktopBuddy.Domain.Persistence;
using DesktopBuddy.Economy;
using DesktopBuddy.Persistence;
using Godot;

namespace DesktopBuddy.Testing;

internal sealed class ManualMonotonicTimeSource : IMonotonicTimeSource
{
    public double Seconds { get; set; }
    public void Advance(double seconds) => Seconds += seconds;
}

internal static class M4LifecycleScenarioSupport
{
    public static async Task<(SandboxRoot Sandbox, InMemoryProgressStore Store)?> Load(
        SceneTree tree,
        ManualMonotonicTimeSource time)
    {
        var packed = GD.Load<PackedScene>("res://scenes/sandbox.tscn");
        if (packed is null || packed.Instantiate() is not SandboxRoot sandbox)
            return null;
        double cashPerPain = sandbox.Pipeline.RequirePainProfile().CashPerPain;
        var progress = new BuddyProgressState(cashPerPain);
        var economy = new EconomyService(progress, CatalogueLoader.Catalogue);
        var store = new InMemoryProgressStore();
        var saves = new SaveCoordinator(progress, store);
        sandbox.Configure(new RunContext(
            progress,
            economy,
            store,
            saves,
            new LocalSettingsSave(),
            SaveLoadStatus.NewSave,
            time));
        tree.Root.AddChild(sandbox);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        // The scenario advances the injected source explicitly; disable engine
        // callbacks so repeated identical timestamps cannot add zero-span noise.
        sandbox.Lifecycle.SetProcess(false);
        return (sandbox, store);
    }

    public static void Sample(LifecycleCoordinator lifecycle) => lifecycle._Process(0.0);

    public static async Task Cleanup(SceneTree tree, SandboxRoot sandbox)
    {
        if (sandbox.Lifecycle.IsHiddenToTray)
            sandbox.SetHiddenToTray(false);
        sandbox.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}

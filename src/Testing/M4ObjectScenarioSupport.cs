using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Testing;

internal static class M4ObjectScenarioSupport
{
    public static async Task<BuddyLab?> LoadLab(SceneTree tree, ulong seed)
    {
        PackedScene? packed = GD.Load<PackedScene>("res://scenes/buddy_lab.tscn");
        if (packed is null)
            return null;
        BuddyLab lab = packed.Instantiate<BuddyLab>();
        tree.Root.AddChild(lab);
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        lab.Controls.Reseed(seed);
        await ScenarioSteps.WaitForStanding(tree, lab, 1800);
        return lab;
    }

    public static async Task<bool> WaitForPhase(
        SceneTree tree,
        BuddyLab lab,
        ObjectPhase phase,
        int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (lab.Buddy.ObjectInteraction.Phase == phase)
                return true;
        }
        return false;
    }

    public static async Task<bool> WaitFor(
        SceneTree tree,
        System.Func<bool> predicate,
        int timeoutTicks)
    {
        for (int tick = 0; tick < timeoutTicks; tick++)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            if (predicate())
                return true;
        }
        return false;
    }

    public static async Task SendKey(SceneTree tree, Key key)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            PhysicalKeycode = key,
            Pressed = true,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey
        {
            PhysicalKeycode = key,
            Pressed = false,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    public static LooseObjectBody? SpawnCatchCandidate(BuddyLab lab)
    {
        Vector2 handsCenter =
            (lab.Buddy.Rig.LeftHand.GlobalPosition + lab.Buddy.Rig.RightHand.GlobalPosition) * 0.5f;
        LooseObjectBody? body = lab.SpawnLooseObject(
            lab.SafeObjectProfile,
            handsCenter + new Vector2(0.0f, -4.0f),
            new Vector2(0.0f, -20.0f),
            playerThrown: false);
        if (body is null || !lab.Grab.TryGrab(body, body.GlobalPosition))
            return null;

        // Exercise the real pointer release bridge: the registry, rather than the
        // scenario, must mint the player throw token consumed by catch care.
        lab.Grab.Release();
        return body;
    }

    public static async Task Cleanup(SceneTree tree, BuddyLab lab)
    {
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}

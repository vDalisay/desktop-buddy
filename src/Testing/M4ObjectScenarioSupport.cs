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

    /// <summary>
    /// Throws a ball at the buddy <b>from a distance</b>, on a ballistic arc aimed at its
    /// chest, through the real grab/release bridge so the registry mints the player throw
    /// token that catch care is paid against.
    ///
    /// <para>This used to spawn the ball at the hands' own midpoint and release it there,
    /// which pre-satisfied every catch condition. The scenario passed while a real thrown
    /// ball bounced off the buddy untouched — the test asserted the mechanism, never the
    /// interaction.</para>
    /// </summary>
    public static LooseObjectBody? SpawnCatchCandidate(BuddyLab lab, float distance = 150.0f)
    {
        Vector2 chest = lab.Buddy.Rig.Torso.GlobalPosition;
        // Throw from whichever side has room inside the room bounds.
        float side = chest.X - lab.Boundaries.InnerBounds.Position.X > distance + 20.0f
            ? -1.0f
            : 1.0f;
        Vector2 spawn = chest + new Vector2(side * distance, -30.0f);
        Vector2 toChest = chest - spawn;
        // Flat, fast, and slightly lifted: it must arrive at the chest, not loop over it.
        Vector2 velocity = new(toChest.X * 1.6f, (toChest.Y * 1.6f) - 40.0f);

        LooseObjectBody? body = lab.SpawnLooseObject(
            lab.SafeObjectProfile,
            spawn,
            Vector2.Zero,
            playerThrown: false);
        if (body is null || !lab.Grab.TryGrab(body, body.GlobalPosition))
            return null;

        lab.Grab.Release();
        body.LinearVelocity = velocity;
        return body;
    }

    public static async Task Cleanup(SceneTree tree, BuddyLab lab)
    {
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}

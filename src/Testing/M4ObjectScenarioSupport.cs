using System.Threading.Tasks;
using DesktopBuddy.App;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Physics;
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

    /// <summary>
    /// Points the player's cursor at a world position through the real pointer path, so a
    /// scenario can assert where a cursor-aimed throw actually lands. Headless runs see no
    /// mouse at all, which parks the cursor at world origin and makes every aimed throw a
    /// near-vertical one — that tests almost nothing about aiming.
    ///
    /// <para>Object action outranks social, so an engaged cursor cannot steal priority from
    /// the lifecycle under test.</para>
    /// </summary>
    public static async Task AimCursorAt(SceneTree tree, BuddyLab lab, Vector2 worldTarget)
    {
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            Position = lab.GetViewport().GetCanvasTransform() * worldTarget,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>A lateral, in-bounds point to aim a return throw at.</summary>
    public static Vector2 LateralCursorTarget(BuddyLab lab, float distance = 170.0f)
    {
        Vector2 chest = lab.Buddy.Rig.Torso.GlobalPosition;
        Rect2 bounds = lab.Boundaries.InnerBounds;
        float side = bounds.End.X - chest.X > distance + 20.0f ? 1.0f : -1.0f;
        return chest + new Vector2(side * distance, -25.0f);
    }

    /// <summary>
    /// Moves the real pointer to a world position through Godot's input queue, so anything
    /// driving the launcher chord exercises the same path a player does.
    /// </summary>
    public static async Task MovePointer(
        SceneTree tree,
        BuddyLab lab,
        Vector2 world,
        MouseButtonMask mask)
    {
        Vector2 viewport = lab.GetViewport().GetCanvasTransform() * world;
        Input.ParseInputEvent(new InputEventMouseMotion
        {
            ButtonMask = mask,
            Position = viewport,
            GlobalPosition = viewport,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
    }

    /// <summary>Presses or releases a real mouse button at a world position.</summary>
    public static async Task SetButton(
        SceneTree tree,
        BuddyLab lab,
        Vector2 world,
        MouseButton button,
        bool pressed,
        MouseButtonMask mask)
    {
        Vector2 viewport = lab.GetViewport().GetCanvasTransform() * world;
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = button,
            ButtonMask = mask,
            Pressed = pressed,
            Position = viewport,
            GlobalPosition = viewport,
        });
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
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

    /// <summary>
    /// Throws a ball at the buddy's chest on an arc that <b>stays off the floor</b>, through
    /// the real grab/release bridge so the registry mints a player throw token.
    ///
    /// <para><see cref="SpawnCatchCandidate"/> deliberately uses a flat hard throw, and over
    /// its ~0.6 s flight gravity pulls the ball down far enough to graze the floor on the way
    /// in. That is a perfectly good catch test, but it is not a <i>clean</i> catch — so any
    /// scenario asserting the caught-out-of-the-air reward needs this instead. The launch is
    /// solved with the same <see cref="ThrowArc"/> the buddy throws with, over a short flight,
    /// which both guarantees arrival at the chest and keeps the whole arc above the straight
    /// line from spawn to target.</para>
    /// </summary>
    public static LooseObjectBody? SpawnCleanThrow(
        BuddyLab lab,
        float distance = 150.0f,
        float flightSeconds = 0.35f)
    {
        Vector2 chest = lab.Buddy.Rig.Torso.GlobalPosition;
        float side = chest.X - lab.Boundaries.InnerBounds.Position.X > distance + 20.0f
            ? -1.0f
            : 1.0f;
        Vector2 spawn = chest + new Vector2(side * distance, -40.0f);

        LooseObjectBody? body = lab.SpawnLooseObject(
            lab.SafeObjectProfile,
            spawn,
            Vector2.Zero,
            playerThrown: false);
        if (body is null || !lab.Grab.TryGrab(body, body.GlobalPosition))
            return null;

        // Release mints the throw token and clears the ground-contact flag.
        lab.Grab.Release();

        float gravity = ProjectSettings
            .GetSetting("physics/2d/default_gravity", 980.0f)
            .AsSingle() * body.GravityScale;
        Vector2 displacement = chest - spawn;
        ThrowArcResult solved = ThrowArc.Solve(
            new System.Numerics.Vector2(displacement.X, displacement.Y),
            gravity,
            body.LinearDamp,
            flightSeconds,
            float.MaxValue);
        body.LinearVelocity = solved.IsValid
            ? new Vector2(solved.Velocity.X, solved.Velocity.Y)
            : new Vector2(displacement.X / flightSeconds, -200.0f);
        return body;
    }

    public static async Task Cleanup(SceneTree tree, BuddyLab lab)
    {
        lab.QueueFree();
        await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }
}

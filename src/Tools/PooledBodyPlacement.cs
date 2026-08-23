using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Puts a pooled physics body where it is supposed to start, and makes the physics server
/// believe it.
///
/// <para>Assigning <c>GlobalPosition</c> to a live <see cref="RigidBody2D"/> from outside
/// <c>_IntegrateForces</c> is advisory: the server keeps its own transform for the body and
/// writes it back over the node's on the next step. A brand new body has no server state to
/// argue with, which is exactly why a pool only shows the fault once it wraps — the second
/// flight of a slot starts from wherever the first one came to rest (owner bug 2026-08-22:
/// shotgun pellets and the pistol magazine appearing at old spawn points after a few shots,
/// instead of at the gun).</para>
///
/// <para>So the state goes to the server directly as well as to the node. Everything pooled
/// and launched — pellets, bullets, dropped magazines, pulled pins — goes through here, so
/// there is one place this rule is stated rather than four copies of it to keep in step.</para>
/// </summary>
public static class PooledBodyPlacement
{
    public static void Launch(
        RigidBody2D body,
        Vector2 position,
        float rotation,
        Vector2 velocity,
        float angularVelocity)
    {
        body.GlobalPosition = position;
        body.Rotation = rotation;
        body.LinearVelocity = velocity;
        body.AngularVelocity = angularVelocity;

        Rid rid = body.GetRid();
        PhysicsServer2D.BodySetState(
            rid, PhysicsServer2D.BodyState.Transform, new Transform2D(rotation, position));
        PhysicsServer2D.BodySetState(rid, PhysicsServer2D.BodyState.LinearVelocity, velocity);
        PhysicsServer2D.BodySetState(rid, PhysicsServer2D.BodyState.AngularVelocity, angularVelocity);

        // The render side has a memory of its own: without this the first drawn frame of the
        // new flight is interpolated from where the slot's last flight ended.
        body.ResetPhysicsInterpolation();
    }
}

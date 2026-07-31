using System;
using DesktopBuddy.Domain.Tools;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace DesktopBuddy.Sandbox;

/// <summary>
/// The camera's own offset lane, used by the real pistol's fire kick. It is a separate
/// component on purpose, for the same reason the bat's hit-lag shake got its own lane
/// (`DECISIONS.md`): an effect that borrows another system's transform ends up entangled
/// with that system's rules, and this one must not be able to interact with
/// <c>ImpactFeedbackPresenter</c>'s slow-time or the bat's whole-game freeze.
///
/// <para>The offset is the deterministic two-frequency wobble
/// <see cref="ChargedSwing.ShakeOffset"/> already defines, on a linear decay envelope, so
/// the same shot on the same tick produces the same camera every run. It is applied to
/// <b>both</b> cameras, because the 2D and 3D presentations are two views of one room and a
/// kick in only one of them would read as a rendering bug.</para>
///
/// <para><b>Non-stacking:</b> a shot during a live kick restarts the envelope. Rapid fire
/// therefore stays inside one envelope's amplitude instead of summing into a shake nobody
/// authored.</para>
/// </summary>
[GlobalClass]
public partial class CameraKickComponent : Node
{
    /// <summary>Wobble frequencies, in Hz. Two, so the motion does not read as a sine.</summary>
    private const float PrimaryHz = 31.0f;
    private const float SecondaryHz = 17.0f;

    [Export] public Camera2D WorldCamera { get; set; } = null!;
    [Export] public Camera3D? WorldCamera3D { get; set; }

    private Vector2 _base2D;
    private Vector3 _base3D;
    private float _amplitude;
    private int _ticks;
    private int _duration;
    private bool _kicking;

    /// <summary>The offset in force right now, in world pixels.</summary>
    public Vector2 CurrentOffsetPx { get; private set; }

    /// <summary>
    /// The largest offset magnitude seen since <see cref="ResetPeak"/>. This is what a
    /// scenario watches to prove rapid fire never stacks into a bigger shake.
    /// </summary>
    public float PeakOffsetPx { get; private set; }

    public int KickCount { get; private set; }

    public bool IsKicking => _kicking;

    public void ResetPeak() => PeakOffsetPx = 0.0f;

    /// <summary>
    /// Starts, or restarts, the kick envelope. An amplitude or duration of zero is a gun
    /// that authors no kick, and is silently nothing rather than an error.
    /// </summary>
    public void Kick(float amplitudePx, int decayTicks)
    {
        if (!float.IsFinite(amplitudePx) || amplitudePx <= 0.0f || decayTicks <= 0)
            return;

        if (!GodotObject.IsInstanceValid(WorldCamera))
            return;

        if (!_kicking)
        {
            // Captured before anything is written, so the kick always unwinds to exactly
            // where the room layout put the camera.
            _base2D = WorldCamera.Position;
            _base3D = GodotObject.IsInstanceValid(WorldCamera3D)
                ? WorldCamera3D!.Position
                : Vector3.Zero;
            _kicking = true;
        }

        _amplitude = amplitudePx;
        _duration = decayTicks;
        _ticks = decayTicks;
        KickCount++;
    }

    /// <summary>
    /// Ends any live kick and leaves both cameras where they are. Called when the room
    /// layout moves the cameras, which invalidates the captured base position.
    /// </summary>
    public void NotifyLayoutChanged()
    {
        _kicking = false;
        _ticks = 0;
        CurrentOffsetPx = Vector2.Zero;
    }

    /// <summary>Advances the envelope on the owning root's routed tick.</summary>
    public void PhysicsTick()
    {
        if (!_kicking)
            return;

        if (_ticks <= 0)
        {
            Restore();
            return;
        }

        float envelope = (float)_ticks / Math.Max(1, _duration);
        float seconds = (float)(_duration - _ticks) / Engine.PhysicsTicksPerSecond;
        NumericsVector2 offset = ChargedSwing.ShakeOffset(
            seconds, _amplitude * envelope, PrimaryHz, SecondaryHz);
        CurrentOffsetPx = new Vector2(offset.X, offset.Y);
        PeakOffsetPx = Mathf.Max(PeakOffsetPx, CurrentOffsetPx.Length());
        Apply(CurrentOffsetPx);
        _ticks--;
    }

    private void Apply(Vector2 offset)
    {
        if (!GodotObject.IsInstanceValid(WorldCamera))
            return;

        WorldCamera.Position = _base2D + offset;
        if (GodotObject.IsInstanceValid(WorldCamera3D))
        {
            // Screen Y is down in 2D and up in 3D: the same shake, mapped the one way
            // this project maps anything crossing that boundary.
            WorldCamera3D!.Position = _base3D + new Vector3(offset.X, -offset.Y, 0.0f);
        }
    }

    private void Restore()
    {
        Apply(Vector2.Zero);
        CurrentOffsetPx = Vector2.Zero;
        _kicking = false;
    }
}

using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// The feather's swing about the player's grip. A spring driven by how fast the pointer moves
/// across the stick, so the plume trails the hand and overshoots on a stop instead of being
/// welded to the cursor (owner instruction 2026-08-19). Shared by the 2D and 3D presentations so
/// both read the same angle; presentation-only, contact never consults it.
/// </summary>
public struct CareToolSway
{
    private const float Stiffness = 140.0f;
    private const float Damping = 13.0f;
    private const float RadiansPerPixelPerSecond = 0.0014f;
    private const float Limit = 0.85f;

    /// <summary>
    /// The wave the feather is swept through while the player holds secondary. Slow and broad
    /// rather than a fast buzz (owner instruction 2026-08-19): a second harmonic at a third of
    /// the amplitude keeps the sweep from reading as a metronome without speeding it up.
    /// </summary>
    private const float WiggleRadians = 0.62f;
    private const float WiggleHz = 1.15f;

    private Vector2 _lastPointer;
    private bool _hasLastPointer;
    private double _wigglePhase;

    /// <summary>Radians to add to the stick's rest angle.</summary>
    public float Angle { get; private set; }

    /// <summary>Angular rate, which the plume tip lags behind by.</summary>
    public float Velocity { get; private set; }

    public void Reset()
    {
        _hasLastPointer = false;
        _wigglePhase = 0.0;
        Angle = 0.0f;
        Velocity = 0.0f;
    }

    public void Tick(Vector2 pointer, float stickAngle, bool wiggling, double delta)
    {
        if (delta <= 0.0)
            return;

        // The shake is driven onto the spring rather than added after it, so the stick keeps its
        // weight: it lags into the shake and rings down out of it instead of snapping.
        if (wiggling)
            _wigglePhase += delta;
        else
            _wigglePhase = 0.0;

        Vector2 velocity = _hasLastPointer ? (pointer - _lastPointer) / (float)delta : Vector2.Zero;
        _lastPointer = pointer;
        _hasLastPointer = true;

        // Cross product of the stick direction with the pointer velocity: sideways motion bends
        // the vane back, motion along the stick barely moves it.
        Vector2 stick = Vector2.FromAngle(stickAngle);
        float lateral = (stick.X * velocity.Y) - (stick.Y * velocity.X);
        float target = Mathf.Clamp(-lateral * RadiansPerPixelPerSecond, -Limit, Limit);
        if (wiggling)
        {
            float sweep = (float)_wigglePhase * Mathf.Tau * WiggleHz;
            target += (Mathf.Sin(sweep) + (Mathf.Sin(sweep * 2.0f) * 0.33f)) * WiggleRadians;
        }

        float step = (float)delta;
        Velocity += ((target - Angle) * Stiffness - (Velocity * Damping)) * step;
        Angle += Velocity * step;
    }
}

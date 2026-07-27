using System;
using System.Numerics;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class ThrowArcTests
{
    private const float Gravity = 980.0f;
    private const float Damping = 1.5f;
    private const float FlightSeconds = 0.55f;
    private const float MaximumSpeed = 720.0f;

    /// <summary>
    /// Integrates the same physics the engine runs (v' = -kv + g) so "lands on the cursor" is
    /// checked against a simulation rather than against the closed form that produced it.
    /// </summary>
    private static Vector2 Simulate(Vector2 velocity, float gravity, float damping, float seconds)
    {
        const float Step = 1.0f / 4000.0f;
        Vector2 position = Vector2.Zero;
        for (float elapsed = 0.0f; elapsed < seconds; elapsed += Step)
        {
            float dt = MathF.Min(Step, seconds - elapsed);
            velocity += ((-damping * velocity) + new Vector2(0.0f, gravity)) * dt;
            position += velocity * dt;
        }

        return position;
    }

    [Theory]
    [InlineData(120.0f, 0.0f)]
    [InlineData(-90.0f, 0.0f)]
    [InlineData(140.0f, -60.0f)]
    [InlineData(60.0f, 80.0f)]
    [InlineData(0.0f, -70.0f)]
    public void SolvedLaunch_LandsOnTheTarget(float deltaX, float deltaY)
    {
        var displacement = new Vector2(deltaX, deltaY);

        ThrowArcResult result = ThrowArc.Solve(
            displacement, Gravity, Damping, FlightSeconds, MaximumSpeed);

        Assert.True(result.IsValid);
        Assert.False(result.Clamped);
        Vector2 landed = Simulate(result.Velocity, Gravity, Damping, FlightSeconds);
        Assert.True(
            Vector2.Distance(landed, displacement) < 1.0f,
            $"landed {landed} expected {displacement}");
    }

    [Fact]
    public void UndampedLaunch_LandsOnTheTarget()
    {
        var displacement = new Vector2(150.0f, -40.0f);

        ThrowArcResult result = ThrowArc.Solve(
            displacement, Gravity, 0.0f, FlightSeconds, MaximumSpeed);

        Assert.True(result.IsValid);
        Vector2 landed = Simulate(result.Velocity, Gravity, 0.0f, FlightSeconds);
        Assert.True(
            Vector2.Distance(landed, displacement) < 1.0f,
            $"landed {landed} expected {displacement}");
    }

    /// <summary>
    /// The point of solving for a duration: even a dead-level throw leaves the hand rising, so
    /// the ball always arcs instead of being fired flat at the cursor.
    /// </summary>
    [Fact]
    public void LevelThrow_LaunchesUpward()
    {
        ThrowArcResult result = ThrowArc.Solve(
            new Vector2(120.0f, 0.0f), Gravity, Damping, FlightSeconds, MaximumSpeed);

        Assert.True(result.IsValid);
        Assert.True(result.Velocity.Y < 0.0f, $"expected upward launch, got {result.Velocity.Y}");
        Assert.True(result.Velocity.X > 0.0f);
    }

    [Fact]
    public void TargetTowardTheCursor_LaunchesTowardTheCursor()
    {
        ThrowArcResult right = ThrowArc.Solve(
            new Vector2(100.0f, 0.0f), Gravity, Damping, FlightSeconds, MaximumSpeed);
        ThrowArcResult left = ThrowArc.Solve(
            new Vector2(-100.0f, 0.0f), Gravity, Damping, FlightSeconds, MaximumSpeed);

        Assert.True(right.Velocity.X > 0.0f);
        Assert.True(left.Velocity.X < 0.0f);
    }

    /// <summary>An out-of-range target still throws the right way, just short.</summary>
    [Fact]
    public void OutOfRangeTarget_IsClampedWithoutChangingDirection()
    {
        var displacement = new Vector2(900.0f, -200.0f);

        ThrowArcResult unclamped = ThrowArc.Solve(
            displacement, Gravity, Damping, FlightSeconds, float.MaxValue);
        ThrowArcResult result = ThrowArc.Solve(
            displacement, Gravity, Damping, FlightSeconds, MaximumSpeed);

        Assert.True(result.IsValid);
        Assert.True(result.Clamped);
        Assert.True(result.Velocity.Length() <= MaximumSpeed + 0.01f);
        Vector2 wanted = Vector2.Normalize(unclamped.Velocity);
        Vector2 actual = Vector2.Normalize(result.Velocity);
        Assert.True(Vector2.Distance(wanted, actual) < 0.001f);
    }

    [Theory]
    [InlineData(0.0f, 1.5f, 0.55f, 720.0f, false)]
    [InlineData(980.0f, 1.5f, 0.0f, 720.0f, true)]
    [InlineData(980.0f, 1.5f, -0.55f, 720.0f, true)]
    [InlineData(980.0f, -1.0f, 0.55f, 720.0f, true)]
    [InlineData(-980.0f, 1.5f, 0.55f, 720.0f, true)]
    [InlineData(980.0f, 1.5f, 0.55f, 0.0f, true)]
    public void DegenerateInputs_AreRejected(
        float gravity, float damping, float flightSeconds, float maximumSpeed, bool expectInvalid)
    {
        ThrowArcResult result = ThrowArc.Solve(
            new Vector2(100.0f, 0.0f), gravity, damping, flightSeconds, maximumSpeed);

        Assert.Equal(!expectInvalid, result.IsValid);
    }

    [Fact]
    public void NonFiniteDisplacement_IsRejected()
    {
        ThrowArcResult result = ThrowArc.Solve(
            new Vector2(float.NaN, 0.0f), Gravity, Damping, FlightSeconds, MaximumSpeed);

        Assert.False(result.IsValid);
    }
}

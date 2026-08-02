using System;
using System.Numerics;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class GrabTetherTests
{
    private const float Tolerance = 0.0005f;

    [Fact]
    public void Evaluate_PullsTowardCursor()
    {
        var input = new GrabTetherInput(
            AnchorError: new Vector2(10.0f, 0.0f),
            RelativeVelocity: Vector2.Zero,
            Stiffness: 20.0f,
            Damping: 0.0f,
            MaximumForce: 100000.0f);

        GrabTetherResult result = GrabTether.Evaluate(input);

        Assert.Equal(200.0f, result.Force.X, Tolerance);
        Assert.Equal(0.0f, result.Force.Y, Tolerance);
        Assert.Equal(10.0f, result.Extension, Tolerance);
        Assert.False(result.ForceClamped);
    }

    [Fact]
    public void Evaluate_DampingOpposesRelativeVelocity()
    {
        var input = new GrabTetherInput(
            AnchorError: Vector2.Zero,
            RelativeVelocity: new Vector2(5.0f, 0.0f),
            Stiffness: 20.0f,
            Damping: 4.0f,
            MaximumForce: 100000.0f);

        GrabTetherResult result = GrabTether.Evaluate(input);

        // No positional error, so force is purely -damping * relativeVelocity.
        Assert.Equal(-20.0f, result.Force.X, Tolerance);
    }

    [Fact]
    public void Evaluate_ClampsForceToMaximum()
    {
        var input = new GrabTetherInput(
            AnchorError: new Vector2(1000.0f, 0.0f),
            RelativeVelocity: Vector2.Zero,
            Stiffness: 500.0f,
            Damping: 0.0f,
            MaximumForce: 6000.0f);

        GrabTetherResult result = GrabTether.Evaluate(input);

        Assert.True(result.ForceClamped);
        Assert.Equal(6000.0f, result.Force.Length(), 0.01f);
    }

    [Fact]
    public void CapReleaseVelocity_BelowCap_Unchanged()
    {
        var velocity = new Vector2(30.0f, 40.0f); // magnitude 50

        Vector2 result = GrabTether.CapReleaseVelocity(velocity, 100.0f);

        Assert.Equal(velocity, result);
    }

    [Fact]
    public void CapReleaseVelocity_AboveCap_ScaledToCapPreservingDirection()
    {
        var velocity = new Vector2(30.0f, 40.0f); // magnitude 50

        Vector2 result = GrabTether.CapReleaseVelocity(velocity, 10.0f);

        Assert.Equal(10.0f, result.Length(), 0.001f);
        // Direction preserved: components scaled by 10/50 = 0.2.
        Assert.Equal(6.0f, result.X, Tolerance);
        Assert.Equal(8.0f, result.Y, Tolerance);
    }

    [Fact]
    public void CapReleaseVelocity_AtCap_Unchanged()
    {
        var velocity = new Vector2(0.0f, 50.0f);

        Vector2 result = GrabTether.CapReleaseVelocity(velocity, 50.0f);

        Assert.Equal(velocity, result);
    }

    [Fact]
    public void CapReleaseVelocity_ZeroVelocity_ReturnsZero()
    {
        Vector2 result = GrabTether.CapReleaseVelocity(Vector2.Zero, 100.0f);

        Assert.Equal(Vector2.Zero, result);
    }

    [Fact]
    public void CapReleaseVelocity_ZeroCap_ClampsAnyMotionToZero()
    {
        Vector2 result = GrabTether.CapReleaseVelocity(new Vector2(3.0f, 4.0f), 0.0f);

        Assert.Equal(0.0f, result.Length(), 0.001f);
    }

    [Fact]
    public void CapReleaseVelocity_NegativeCap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => GrabTether.CapReleaseVelocity(new Vector2(1.0f, 0.0f), -1.0f));
    }

    [Theory]
    [InlineData(float.NaN, 0.0f)]
    [InlineData(float.PositiveInfinity, 0.0f)]
    [InlineData(0.0f, float.NegativeInfinity)]
    public void CapReleaseVelocity_NonFiniteVelocity_IsADeadDrop(float x, float y)
    {
        // Releasing a glitched velocity would write NaN into the body's position and take
        // the rest of the run with it. Both grab variants route through here.
        Vector2 result = GrabTether.CapReleaseVelocity(new Vector2(x, y), 900.0f);

        Assert.Equal(Vector2.Zero, result);
    }

    [Fact]
    public void CapReleaseVelocity_PoweredThrow_BeatsTheNormalCapAndStopsAtItsOwn()
    {
        // M5 §3.2: a Power throw multiplies first, then caps against its own higher ceiling.
        const float normalCap = 900.0f;
        const float powerCap = 1_300.0f;
        const float multiplier = 1.6f;
        var velocity = new Vector2(600.0f, -800.0f); // 1000 px/s, already over the normal cap

        Vector2 normal = GrabTether.CapReleaseVelocity(velocity, normalCap);
        Vector2 powered = GrabTether.CapReleaseVelocity(velocity * multiplier, powerCap);

        Assert.True(powered.Length() > normal.Length());
        Assert.True(powered.Length() <= powerCap);
        Assert.Equal(powerCap, powered.Length(), 0.01f);

        // Direction survives the multiply-then-cap ordering.
        Vector2 normalDirection = Vector2.Normalize(normal);
        Vector2 poweredDirection = Vector2.Normalize(powered);
        Assert.Equal(normalDirection.X, poweredDirection.X, 0.0001f);
        Assert.Equal(normalDirection.Y, poweredDirection.Y, 0.0001f);
    }

    [Fact]
    public void CapReleaseVelocity_PoweredThrowBelowItsCap_KeepsTheFullMultipliedSpeed()
    {
        var velocity = new Vector2(300.0f, 0.0f);

        Vector2 powered = GrabTether.CapReleaseVelocity(velocity * 1.6f, 1_300.0f);

        Assert.Equal(480.0f, powered.Length(), 0.01f);
    }
}

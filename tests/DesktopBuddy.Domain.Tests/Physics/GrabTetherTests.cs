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
}

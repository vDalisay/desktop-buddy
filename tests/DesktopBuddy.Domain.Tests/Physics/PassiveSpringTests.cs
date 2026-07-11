using System.Numerics;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class PassiveSpringTests
{
    [Fact]
    public void RestOffsetAndMatchingVelocityProduceNoForce()
    {
        PassiveSpringResult result = Evaluate(actual: new Vector2(10, -4), rest: new Vector2(10, -4));

        Assert.Equal(Vector2.Zero, result.ForceOnA);
        Assert.False(result.LimitActive);
        Assert.False(result.ForceClamped);
    }

    [Fact]
    public void DisplacementPullsBothEndpointsTowardRestRelationship()
    {
        PassiveSpringResult result = Evaluate(actual: new Vector2(14, 0), rest: new Vector2(10, 0));
        Vector2 forceOnB = -result.ForceOnA;

        Assert.Equal(new Vector2(400, 0), result.ForceOnA);
        Assert.Equal(Vector2.Zero, result.ForceOnA + forceOnB);
    }

    [Fact]
    public void CompressionPushesEndpointAOutward()
    {
        PassiveSpringResult result = Evaluate(actual: new Vector2(6, 0), rest: new Vector2(10, 0));

        Assert.Equal(new Vector2(-400, 0), result.ForceOnA);
    }

    [Fact]
    public void DampingOpposesRelativeSeparation()
    {
        var input = new PassiveSpringInput(
            new Vector2(10, 0), new Vector2(3, -2), new Vector2(10, 0),
            Stiffness: 100, Damping: 10, MaximumDistance: 20,
            LimitStiffness: 200, MaximumForce: 10_000);

        PassiveSpringResult result = PassiveSpring.Evaluate(input);

        Assert.Equal(new Vector2(30, -20), result.ForceOnA);
    }

    [Fact]
    public void MaximumDistanceAddsSoftLimitResponse()
    {
        PassiveSpringResult result = Evaluate(actual: new Vector2(25, 0), rest: new Vector2(25, 0));

        Assert.True(result.LimitActive);
        Assert.Equal(new Vector2(1_000, 0), result.ForceOnA);
        Assert.Equal(1.25f, result.Strain, 3);
    }

    [Fact]
    public void MaximumForceClampsVectorMagnitudeWithoutChangingDirection()
    {
        var input = new PassiveSpringInput(
            new Vector2(100, 100), Vector2.Zero, Vector2.Zero,
            Stiffness: 100, Damping: 0, MaximumDistance: 1_000,
            LimitStiffness: 0, MaximumForce: 500);

        PassiveSpringResult result = PassiveSpring.Evaluate(input);

        Assert.True(result.ForceClamped);
        Assert.Equal(500.0f, result.ForceOnA.Length(), 3);
        Assert.Equal(result.ForceOnA.X, result.ForceOnA.Y, 3);
    }

    private static PassiveSpringResult Evaluate(Vector2 actual, Vector2 rest)
    {
        var input = new PassiveSpringInput(
            actual, Vector2.Zero, rest,
            Stiffness: 100, Damping: 10, MaximumDistance: 20,
            LimitStiffness: 200, MaximumForce: 10_000);
        return PassiveSpring.Evaluate(input);
    }
}

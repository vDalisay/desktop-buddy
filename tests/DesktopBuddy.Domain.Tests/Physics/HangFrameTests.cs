using System;
using System.Numerics;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class HangFrameTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void HeadGrabFromStandingKeepsZeroFrameAngle()
    {
        HangFrameResult result = Evaluate(new Vector2(0.0f, 50.0f), new Vector2(0.0f, 80.0f));

        Assert.True(result.IsValid);
        Assert.Equal(0.0f, result.Angle, Tolerance);
    }

    [Theory]
    [InlineData(1.0f, 3.1415927f)]
    [InlineData(-1.0f, -3.1415927f)]
    public void FootGrabWithMassBelowProducesSignConsistentHalfTurn(
        float actualX,
        float expectedAngle)
    {
        HangFrameResult result = Evaluate(
            new Vector2(0.0f, -50.0f),
            new Vector2(actualX * 0.001f, 80.0f));

        Assert.True(result.IsValid);
        Assert.Equal(expectedAngle, result.Angle, 0.001f);
    }

    [Fact]
    public void TorsoGrabIsValidWhenRestCenterOfMassIsOffset()
    {
        HangFrameResult result = Evaluate(new Vector2(0.0f, 3.0f), new Vector2(0.0f, 40.0f));

        Assert.True(result.IsValid);
        Assert.Equal(0.0f, result.Angle, Tolerance);
    }

    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f, 20.0f)]
    [InlineData(0.5f, 0.0f, 0.0f, 20.0f)]
    [InlineData(0.0f, 20.0f, 0.5f, 0.0f)]
    public void DegenerateDirectionIsInvalid(float restX, float restY, float actualX, float actualY)
    {
        HangFrameResult result = Evaluate(
            new Vector2(restX, restY),
            new Vector2(actualX, actualY));

        Assert.False(result.IsValid);
        Assert.Equal(0.0f, result.Angle);
    }

    [Fact]
    public void NonFiniteDirectionIsInvalid()
    {
        HangFrameResult result = Evaluate(
            new Vector2(float.NaN, 3.0f),
            new Vector2(4.0f, 5.0f));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void DifferenceWrapsAcrossPositivePiSeam()
    {
        Vector2 rest = DirectionAtDegrees(179.0f);
        Vector2 actual = DirectionAtDegrees(-179.0f);

        HangFrameResult result = Evaluate(rest, actual);

        Assert.True(result.IsValid);
        Assert.Equal(2.0f * MathF.PI / 180.0f, result.Angle, Tolerance);
    }

    [Fact]
    public void DifferenceWrapsAcrossNegativePiSeam()
    {
        Vector2 rest = DirectionAtDegrees(-179.0f);
        Vector2 actual = DirectionAtDegrees(179.0f);

        HangFrameResult result = Evaluate(rest, actual);

        Assert.True(result.IsValid);
        Assert.Equal(-2.0f * MathF.PI / 180.0f, result.Angle, Tolerance);
    }

    private static HangFrameResult Evaluate(Vector2 rest, Vector2 actual) =>
        HangFrame.Evaluate(new HangFrameInput(rest, actual));

    private static Vector2 DirectionAtDegrees(float degrees)
    {
        float radians = degrees * MathF.PI / 180.0f;
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians)) * 10.0f;
    }
}

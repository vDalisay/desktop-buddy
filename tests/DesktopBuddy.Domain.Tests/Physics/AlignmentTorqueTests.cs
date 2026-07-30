using System;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class AlignmentTorqueTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void AlignedToolIsLeftAlone()
    {
        AlignmentTorqueResult result = Evaluate(angleError: 0.0f);

        Assert.True(result.IsValid);
        Assert.Equal(0.0f, result.Torque, Tolerance);
        Assert.False(result.WasClamped);
    }

    [Fact]
    public void ProportionalTermFollowsTheWrappedError()
    {
        AlignmentTorqueResult result = Evaluate(
            angleError: 0.5f,
            stiffness: 20.0f,
            damping: 0.0f,
            maximumTorque: 1000.0f);

        Assert.True(result.IsValid);
        Assert.Equal(10.0f, result.Torque, Tolerance);
        Assert.Equal(0.5f, result.WrappedError, Tolerance);
    }

    [Fact]
    public void DampingOpposesSpin()
    {
        AlignmentTorqueResult result = Evaluate(
            angleError: 0.0f,
            stiffness: 20.0f,
            damping: 3.0f,
            angularVelocity: 2.0f,
            maximumTorque: 1000.0f);

        Assert.True(result.IsValid);
        Assert.Equal(-6.0f, result.Torque, Tolerance);
    }

    [Theory]
    [InlineData(0.4f, 1)]
    [InlineData(-0.4f, -1)]
    public void TorqueTurnsTowardTheTarget(float angleError, int expectedSign)
    {
        AlignmentTorqueResult result = Evaluate(angleError);

        Assert.True(result.IsValid);
        Assert.Equal(expectedSign, MathF.Sign(result.Torque));
    }

    /// <summary>
    /// A swing that crosses the winding boundary must take the short way. An
    /// unwrapped error of nearly a full turn is really a small turn the other way.
    /// </summary>
    [Fact]
    public void ErrorPastHalfATurnTakesTheShortWayAround()
    {
        AlignmentTorqueResult result = Evaluate(
            angleError: MathF.Tau - 0.25f,
            stiffness: 10.0f,
            damping: 0.0f,
            maximumTorque: 1000.0f);

        Assert.True(result.IsValid);
        Assert.Equal(-0.25f, result.WrappedError, Tolerance);
        Assert.Equal(-2.5f, result.Torque, Tolerance);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    public void TorqueIsBoundedInBothDirections(float sign)
    {
        AlignmentTorqueResult result = Evaluate(
            angleError: sign * 1.5f,
            stiffness: 10_000.0f,
            damping: 0.0f,
            maximumTorque: 42.0f);

        Assert.True(result.IsValid);
        Assert.True(result.WasClamped);
        Assert.Equal(sign * 42.0f, result.Torque, Tolerance);
    }

    /// <summary>Zero stiffness is how a circular tool authors "never align".</summary>
    [Fact]
    public void ZeroStiffnessAndDampingProducesNoTorque()
    {
        AlignmentTorqueResult result = Evaluate(
            angleError: 1.2f,
            stiffness: 0.0f,
            damping: 0.0f,
            angularVelocity: 9.0f,
            maximumTorque: 1000.0f);

        Assert.True(result.IsValid);
        Assert.Equal(0.0f, result.Torque, Tolerance);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 10.0f, 1.0f, 100.0f)]
    [InlineData(0.5f, float.PositiveInfinity, 10.0f, 1.0f, 100.0f)]
    [InlineData(0.5f, 0.0f, float.NaN, 1.0f, 100.0f)]
    [InlineData(0.5f, 0.0f, -1.0f, 1.0f, 100.0f)]
    [InlineData(0.5f, 0.0f, 10.0f, -1.0f, 100.0f)]
    [InlineData(0.5f, 0.0f, 10.0f, 1.0f, 0.0f)]
    [InlineData(0.5f, 0.0f, 10.0f, 1.0f, float.NaN)]
    public void MalformedInputIsRejectedInsteadOfPoisoningTheBody(
        float angleError,
        float angularVelocity,
        float stiffness,
        float damping,
        float maximumTorque)
    {
        AlignmentTorqueResult result = AlignmentTorque.Evaluate(new AlignmentTorqueInput(
            angleError, angularVelocity, stiffness, damping, maximumTorque));

        Assert.False(result.IsValid);
        Assert.Equal(0.0f, result.Torque);
        Assert.False(result.WasClamped);
    }

    [Fact]
    public void SwingAngleIsSquareToTheDirectionOfTravel()
    {
        (float angle, bool hasTarget) = AlignmentTorque.SwingAngleFor(
            velocityX: 100.0f, velocityY: 0.0f, minimumSpeed: 1.0f);

        Assert.True(hasTarget);
        Assert.Equal(MathF.PI * 0.5f, angle, Tolerance);
    }

    [Fact]
    public void AStillToolHasNoSwingToAlignTo()
    {
        (float angle, bool hasTarget) = AlignmentTorque.SwingAngleFor(
            velocityX: 0.3f, velocityY: 0.4f, minimumSpeed: 1.0f);

        Assert.False(hasTarget);
        Assert.Equal(0.0f, angle);
    }

    [Fact]
    public void MalformedVelocityHasNoSwingTarget()
    {
        (_, bool hasTarget) = AlignmentTorque.SwingAngleFor(
            velocityX: float.NaN, velocityY: 0.0f, minimumSpeed: 1.0f);

        Assert.False(hasTarget);
    }

    /// <summary>
    /// The bat is symmetric about its center, so being a half turn from the target
    /// is being on target — it must never spin 180° to present its other end.
    /// </summary>
    [Theory]
    [InlineData(MathF.PI, 0.0f)]
    [InlineData(-MathF.PI, 0.0f)]
    public void HalfATurnIsAlreadyAlignedForATwoEndedTool(float target, float expected)
    {
        float error = AlignmentTorque.SymmetricError(target, currentAngle: 0.0f);

        Assert.Equal(expected, error, Tolerance);
    }

    [Theory]
    [InlineData(0.3f, 0.3f)]
    [InlineData(-0.3f, -0.3f)]
    [InlineData(MathF.PI - 0.2f, -0.2f)]
    [InlineData(-MathF.PI + 0.2f, 0.2f)]
    public void SymmetricErrorAlwaysTakesTheNearerEnd(float target, float expected)
    {
        float error = AlignmentTorque.SymmetricError(target, currentAngle: 0.0f);

        Assert.Equal(expected, error, Tolerance);
        Assert.True(MathF.Abs(error) <= (MathF.PI * 0.5f) + Tolerance);
    }

    private static AlignmentTorqueResult Evaluate(
        float angleError,
        float stiffness = 10.0f,
        float damping = 1.0f,
        float angularVelocity = 0.0f,
        float maximumTorque = 1000.0f) =>
        AlignmentTorque.Evaluate(new AlignmentTorqueInput(
            angleError, angularVelocity, stiffness, damping, maximumTorque));
}

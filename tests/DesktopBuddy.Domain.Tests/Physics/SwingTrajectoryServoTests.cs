using System;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

/// <summary>
/// The servo that drives the home-run arc. Its whole reason to exist is the
/// velocity-error damping proven below: the ordinary alignment servo would
/// fight the commanded swing, so the bat would arrive slow and every measured
/// tip-speed envelope would be reading a saturated servo instead of the charge.
/// </summary>
public sealed class SwingTrajectoryServoTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void AnOnTrajectoryBodyIsLeftAlone()
    {
        SwingTrajectoryServoResult result = Evaluate(
            angleError: 0.0f,
            angularVelocity: 40.0f,
            targetAngularVelocity: 40.0f);

        Assert.True(result.IsValid);
        Assert.Equal(0.0f, result.Torque, Tolerance);
        Assert.Equal(0.0f, result.VelocityError, Tolerance);
    }

    /// <summary>
    /// The distinction from <see cref="AlignmentTorque"/>, stated as a test: a
    /// body spinning at exactly the commanded rate must receive no braking
    /// torque, where an absolute-velocity damper would brake it hard.
    /// </summary>
    [Fact]
    public void CommandedSpinIsNotDampedTheWayAlignmentWouldDampIt()
    {
        SwingTrajectoryServoResult swing = Evaluate(
            angleError: 0.0f,
            angularVelocity: 66.0f,
            targetAngularVelocity: 66.0f,
            damping: 500.0f);

        AlignmentTorqueResult alignment = AlignmentTorque.Evaluate(new AlignmentTorqueInput(
            AngleError: 0.0f,
            AngularVelocity: 66.0f,
            Stiffness: 900.0f,
            Damping: 500.0f,
            MaximumTorque: 2_000_000.0f));

        Assert.Equal(0.0f, swing.Torque, Tolerance);
        Assert.Equal(-33_000.0f, alignment.Torque, 0.5f);
    }

    [Fact]
    public void ABodyLaggingTheCommandedRateIsPushedForward()
    {
        SwingTrajectoryServoResult result = Evaluate(
            angleError: 0.0f,
            angularVelocity: 10.0f,
            targetAngularVelocity: 66.0f,
            damping: 100.0f);

        Assert.Equal(56.0f, result.VelocityError, Tolerance);
        Assert.Equal(5600.0f, result.Torque, Tolerance);
    }

    [Fact]
    public void ABodyOutrunningTheCommandedRateIsHeldBack()
    {
        SwingTrajectoryServoResult result = Evaluate(
            angleError: 0.0f,
            angularVelocity: 90.0f,
            targetAngularVelocity: 66.0f,
            damping: 100.0f);

        Assert.True(result.Torque < 0.0f);
    }

    [Fact]
    public void ThePositionTermFollowsTheWrappedError()
    {
        SwingTrajectoryServoResult result = Evaluate(
            angleError: 0.25f,
            stiffness: 400.0f,
            damping: 0.0f);

        Assert.Equal(0.25f, result.WrappedError, Tolerance);
        Assert.Equal(100.0f, result.Torque, Tolerance);
    }

    /// <summary>A sweep that crosses a half turn must not unwind the long way.</summary>
    [Fact]
    public void AnArcCrossingHalfATurnTakesTheShortWay()
    {
        SwingTrajectoryServoResult result = Evaluate(
            angleError: MathF.Tau - 0.2f,
            stiffness: 100.0f,
            damping: 0.0f);

        Assert.Equal(-0.2f, result.WrappedError, Tolerance);
        Assert.True(result.Torque < 0.0f);
    }

    [Theory]
    [InlineData(1.0f)]
    [InlineData(-1.0f)]
    public void TorqueIsBoundedInBothDirections(float sign)
    {
        SwingTrajectoryServoResult result = Evaluate(
            angleError: sign * 1.2f,
            stiffness: 5_000_000.0f,
            maximumTorque: 2_000_000.0f);

        Assert.True(result.WasClamped);
        Assert.Equal(sign * 2_000_000.0f, result.Torque, 0.5f);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f, 900.0f, 100.0f, 2000.0f)]
    [InlineData(0.2f, float.PositiveInfinity, 0.0f, 900.0f, 100.0f, 2000.0f)]
    [InlineData(0.2f, 0.0f, float.NaN, 900.0f, 100.0f, 2000.0f)]
    [InlineData(0.2f, 0.0f, 0.0f, -1.0f, 100.0f, 2000.0f)]
    [InlineData(0.2f, 0.0f, 0.0f, 900.0f, float.NaN, 2000.0f)]
    [InlineData(0.2f, 0.0f, 0.0f, 900.0f, 100.0f, 0.0f)]
    public void MalformedInputIsRejectedInsteadOfPoisoningTheBody(
        float angleError,
        float angularVelocity,
        float targetAngularVelocity,
        float stiffness,
        float damping,
        float maximumTorque)
    {
        SwingTrajectoryServoResult result = SwingTrajectoryServo.Evaluate(
            new SwingTrajectoryServoInput(
                angleError,
                angularVelocity,
                targetAngularVelocity,
                stiffness,
                damping,
                maximumTorque));

        Assert.False(result.IsValid);
        Assert.Equal(0.0f, result.Torque);
        Assert.False(result.WasClamped);
    }

    /// <summary>
    /// The authored bat at full charge: <c>6 kg</c>, <c>66.3 rad/s</c>, handle to
    /// centre of mass <c>38 px</c>. This is the number that says the ordinary
    /// follow tether cap of <c>120 000</c> would saturate roughly eightfold, and
    /// that even the uncharged swing sits inside it by under 12%.
    /// </summary>
    [Fact]
    public void AHandlePivotCostsFarMoreThanTheOrdinaryFollowTether()
    {
        float full = SwingTrajectoryServo.PivotHoldForce(6.0f, 5500.0f / 83.0f, 38.0f);
        float uncharged = SwingTrajectoryServo.PivotHoldForce(6.0f, 1800.0f / 83.0f, 38.0f);

        Assert.InRange(full, 990_000.0f, 1_010_000.0f);
        Assert.InRange(uncharged, 100_000.0f, 115_000.0f);
        Assert.True(full > 120_000.0f * 8.0f);
        Assert.True(uncharged < 120_000.0f);
    }

    [Fact]
    public void PivotHoldForceGrowsWithTheSquareOfTheSpin()
    {
        float single = SwingTrajectoryServo.PivotHoldForce(6.0f, 20.0f, 38.0f);
        float doubled = SwingTrajectoryServo.PivotHoldForce(6.0f, 40.0f, 38.0f);

        Assert.Equal(single * 4.0f, doubled, 0.5f);
    }

    [Theory]
    [InlineData(0.0f, 20.0f, 38.0f)]
    [InlineData(6.0f, float.NaN, 38.0f)]
    [InlineData(6.0f, 20.0f, -1.0f)]
    public void MalformedPivotGeometryCostsNothing(float mass, float omega, float radius)
    {
        Assert.Equal(0.0f, SwingTrajectoryServo.PivotHoldForce(mass, omega, radius));
    }

    private static SwingTrajectoryServoResult Evaluate(
        float angleError,
        float angularVelocity = 0.0f,
        float targetAngularVelocity = 0.0f,
        float stiffness = 900.0f,
        float damping = 100.0f,
        float maximumTorque = 2_000_000.0f) =>
        SwingTrajectoryServo.Evaluate(new SwingTrajectoryServoInput(
            angleError,
            angularVelocity,
            targetAngularVelocity,
            stiffness,
            damping,
            maximumTorque));
}

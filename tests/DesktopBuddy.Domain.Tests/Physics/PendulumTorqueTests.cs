using System;
using DesktopBuddy.Domain.Physics;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Physics;

public sealed class PendulumTorqueTests
{
    private const float Tolerance = 0.0001f;

    [Fact]
    public void EquilibriumHasNoRestoringTorque()
    {
        PendulumTorqueResult result = Evaluate(angleError: 0.0f);

        Assert.True(result.IsValid);
        Assert.Equal(0.0f, result.Torque, Tolerance);
        Assert.False(result.WasClamped);
    }

    [Fact]
    public void QuarterTurnProducesMaximumUnclampedGravityResponse()
    {
        PendulumTorqueResult result = Evaluate(
            angleError: MathF.PI * 0.5f,
            totalMass: 2.0f,
            armLength: 3.0f,
            gravityGain: 4.0f,
            maximumTorque: 100.0f);

        Assert.True(result.IsValid);
        Assert.Equal(24.0f, result.Torque, Tolerance);
    }

    [Theory]
    [InlineData(0.75f, 1.0f)]
    [InlineData(-0.75f, -1.0f)]
    public void RestoringSignFollowsWrappedAngleError(float angleError, float expectedSign)
    {
        PendulumTorqueResult result = Evaluate(angleError);

        Assert.True(result.IsValid);
        Assert.Equal(expectedSign, MathF.Sign(result.Torque));
    }

    [Fact]
    public void DampingOpposesAngularVelocity()
    {
        PendulumTorqueResult result = Evaluate(
            angleError: 0.0f,
            angularVelocity: 3.0f,
            swingDamping: 5.0f);

        Assert.True(result.IsValid);
        Assert.Equal(-15.0f, result.Torque, Tolerance);
    }

    [Fact]
    public void ErrorWrapsAcrossPositivePiSeam()
    {
        PendulumTorqueResult wrapped = Evaluate(angleError: 0.2f);
        PendulumTorqueResult acrossSeam = Evaluate(angleError: MathF.Tau + 0.2f);

        Assert.True(acrossSeam.IsValid);
        Assert.Equal(wrapped.Torque, acrossSeam.Torque, Tolerance);
    }

    [Fact]
    public void TorqueIsClampedSymmetrically()
    {
        PendulumTorqueResult positive = Evaluate(
            angleError: MathF.PI * 0.5f,
            maximumTorque: 5.0f);
        PendulumTorqueResult negative = Evaluate(
            angleError: -MathF.PI * 0.5f,
            maximumTorque: 5.0f);

        Assert.Equal(5.0f, positive.Torque, Tolerance);
        Assert.Equal(-5.0f, negative.Torque, Tolerance);
        Assert.True(positive.WasClamped);
        Assert.True(negative.WasClamped);
    }

    [Theory]
    [InlineData(0.0f, 10.0f, 1.0f, 100.0f)]
    [InlineData(2.0f, 0.0f, 1.0f, 100.0f)]
    [InlineData(2.0f, 10.0f, -1.0f, 100.0f)]
    [InlineData(2.0f, 10.0f, 1.0f, 0.0f)]
    public void DegeneratePhysicalInputsAreInvalid(
        float totalMass,
        float armLength,
        float gravityGain,
        float maximumTorque)
    {
        PendulumTorqueResult result = Evaluate(
            angleError: 0.5f,
            totalMass: totalMass,
            armLength: armLength,
            gravityGain: gravityGain,
            maximumTorque: maximumTorque);

        Assert.False(result.IsValid);
        Assert.Equal(0.0f, result.Torque);
    }

    [Fact]
    public void NonFiniteInputIsInvalid()
    {
        PendulumTorqueResult result = Evaluate(angleError: float.NaN);

        Assert.False(result.IsValid);
    }

    private static PendulumTorqueResult Evaluate(
        float angleError,
        float totalMass = 1.0f,
        float armLength = 1.0f,
        float gravityGain = 10.0f,
        float angularVelocity = 0.0f,
        float swingDamping = 0.0f,
        float maximumTorque = 1_000.0f) =>
        PendulumTorque.Evaluate(new PendulumTorqueInput(
            angleError,
            totalMass,
            armLength,
            gravityGain,
            angularVelocity,
            swingDamping,
            maximumTorque));
}

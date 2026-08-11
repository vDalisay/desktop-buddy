using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class VelocityRotationResponseTests
{
    [Fact]
    public void Ordinary_speed_uses_restrained_rotation()
    {
        float scale = VelocityRotationResponse.Scale(
            speed: 20.0f,
            deadband: 4.0f,
            ordinaryScale: 0.28f,
            fullResponseSpeed: 180.0f);

        Assert.InRange(scale, 0.28f, 0.30f);
    }

    [Fact]
    public void High_speed_restores_full_rotation()
    {
        float scale = VelocityRotationResponse.Scale(
            speed: 180.0f,
            deadband: 4.0f,
            ordinaryScale: 0.28f,
            fullResponseSpeed: 180.0f);

        Assert.Equal(1.0f, scale, 5);
    }

    [Fact]
    public void Response_is_monotonic_between_walk_and_impact_speeds()
    {
        float previous = 0.0f;
        for (int speed = 0; speed <= 220; speed += 10)
        {
            float current = VelocityRotationResponse.Scale(speed, 4.0f, 0.28f, 180.0f);
            Assert.True(current >= previous);
            previous = current;
        }
    }

    [Fact]
    public void Invalid_configuration_is_rejected()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            VelocityRotationResponse.Scale(20.0f, -1.0f, 0.28f, 180.0f));
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            VelocityRotationResponse.Scale(20.0f, 4.0f, 1.2f, 180.0f));
        Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            VelocityRotationResponse.Scale(20.0f, 4.0f, 0.28f, 4.0f));
    }
}

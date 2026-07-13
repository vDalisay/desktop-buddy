using System;
using DesktopBuddy.Domain.Buddy;
using DesktopBuddy.Domain.Damage;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Damage;

public sealed class PayoutRegionTests
{
    [Theory]
    [InlineData(BuddyPart.Head, PayoutRegion.Head)]
    [InlineData(BuddyPart.Torso, PayoutRegion.Torso)]
    [InlineData(BuddyPart.LeftHand, PayoutRegion.Arms)]
    [InlineData(BuddyPart.RightHand, PayoutRegion.Arms)]
    [InlineData(BuddyPart.LeftFoot, PayoutRegion.Legs)]
    [InlineData(BuddyPart.RightFoot, PayoutRegion.Legs)]
    public void Of_MapsPartToRegion(BuddyPart part, PayoutRegion expected) =>
        Assert.Equal(expected, PayoutRegions.Of(part));
}

public sealed class PainCurveTests
{
    private const float Tolerance = 0.0005f;

    // A soft floor (0 pain up to impulse 10) then linear to 100 pain at impulse 500.
    private static PainCurve Curve() => new(new[]
    {
        new PainAnchor(0.0f, 0.0f),
        new PainAnchor(10.0f, 0.0f),
        new PainAnchor(500.0f, 100.0f),
    });

    [Fact]
    public void PainFor_BelowFloor_IsZero()
    {
        Assert.Equal(0.0f, Curve().PainFor(5.0f), Tolerance);
        Assert.Equal(0.0f, Curve().PainFor(-3.0f), Tolerance);
    }

    [Fact]
    public void PainFor_AtFloorEdge_IsZero() =>
        Assert.Equal(0.0f, Curve().PainFor(10.0f), Tolerance);

    [Fact]
    public void PainFor_Interpolates_Linearly()
    {
        // Halfway between impulse 10 (pain 0) and 500 (pain 100) = impulse 255 → pain 50.
        Assert.Equal(50.0f, Curve().PainFor(255.0f), Tolerance);
    }

    [Fact]
    public void PainFor_SaturatesAboveLastAnchor() =>
        Assert.Equal(100.0f, Curve().PainFor(9000.0f), Tolerance);

    [Fact]
    public void PainFor_IsMonotonicNonDecreasing()
    {
        PainCurve curve = Curve();
        float previous = -1.0f;
        for (float impulse = 0.0f; impulse <= 600.0f; impulse += 7.0f)
        {
            float pain = curve.PainFor(impulse);
            Assert.True(pain >= previous, $"pain dropped at impulse {impulse}");
            previous = pain;
        }
    }

    [Fact]
    public void Constructor_RejectsNonIncreasingImpulse() =>
        Assert.Throws<ArgumentException>(() => new PainCurve(new[]
        {
            new PainAnchor(10.0f, 0.0f),
            new PainAnchor(10.0f, 5.0f),
        }));

    [Fact]
    public void Constructor_RejectsDecreasingPain() =>
        Assert.Throws<ArgumentException>(() => new PainCurve(new[]
        {
            new PainAnchor(0.0f, 10.0f),
            new PainAnchor(20.0f, 5.0f),
        }));

    [Fact]
    public void Constructor_RejectsFewerThanTwoAnchors() =>
        Assert.Throws<ArgumentException>(() => new PainCurve(new[] { new PainAnchor(0.0f, 0.0f) }));
}

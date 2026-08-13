using System;
using System.Linq;
using DesktopBuddy.Domain.Painting;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Painting;

public sealed class PaintAlgorithmTests
{
    [Fact]
    public void SprayPattern_IsDeterministicAndInsideUnitDisk()
    {
        PaintPoint[] first = SprayPattern.SampleUnitDisk(123456789UL, 128);
        PaintPoint[] second = SprayPattern.SampleUnitDisk(123456789UL, 128);

        Assert.Equal(first, second);
        foreach (PaintPoint point in first)
            Assert.True((point.X * point.X) + (point.Y * point.Y) <= 1.000000000001);
    }

    /// <summary>
    /// Callers step the pulse seed by the golden-ratio constant; consecutive pulses must not be the
    /// same dots shifted by one, or a held spray keeps repainting the pixels it already covered.
    /// </summary>
    [Fact]
    public void SprayPattern_ConsecutivePulseSeeds_DoNotRepeatPoints()
    {
        const ulong step = 0x9E3779B97F4A7C15UL;
        PaintPoint[] first = SprayPattern.SampleUnitDisk(1000UL, 64);
        PaintPoint[] second = SprayPattern.SampleUnitDisk(1000UL + step, 64);

        int shared = first.Intersect(second).Count();
        Assert.True(shared <= 1, $"{shared} of 64 sprayed points repeated between pulses.");
    }

    [Fact]
    public void SprayPattern_DensityGrowsWithEnvelopeArea()
    {
        int small = SprayPattern.PointCountForDiameter(8);
        int medium = SprayPattern.PointCountForDiameter(24);
        int large = SprayPattern.PointCountForDiameter(96);

        Assert.True(small < medium);
        Assert.True(medium < large);
    }

    [Fact]
    public void StraightCurve_IsExactlyCollinear()
    {
        PaintPoint start = new(2, 4);
        PaintPoint end = new(14, 10);
        CubicPaintCurve curve = ClassicCurveGeometry.Straight(start, end);

        for (int index = 0; index <= 20; index++)
        {
            double t = index / 20.0;
            PaintPoint expected = start + ((end - start) * t);
            PaintPoint actual = curve.Evaluate(t);
            Assert.Equal(expected.X, actual.X, 10);
            Assert.Equal(expected.Y, actual.Y, 10);
        }
    }

    [Fact]
    public void OneBend_HitsRequestedConstraint()
    {
        PaintPoint start = new(0, 0);
        PaintPoint end = new(100, 0);
        PaintCurveBend bend = new(0.35, new PaintPoint(35, 20));

        CubicPaintCurve curve = ClassicCurveGeometry.BendOnce(start, end, bend);
        PaintPoint actual = curve.Evaluate(bend.T);

        Assert.Equal(bend.Target.X, actual.X, 8);
        Assert.Equal(bend.Target.Y, actual.Y, 8);
    }

    [Fact]
    public void TwoBends_HitBothRequestedConstraints()
    {
        PaintPoint start = new(0, 0);
        PaintPoint end = new(100, 0);
        PaintCurveBend first = new(0.30, new PaintPoint(30, 20));
        PaintCurveBend second = new(0.72, new PaintPoint(72, -15));

        CubicPaintCurve curve = ClassicCurveGeometry.BendTwice(start, end, first, second);
        PaintPoint actualFirst = curve.Evaluate(first.T);
        PaintPoint actualSecond = curve.Evaluate(second.T);

        Assert.Equal(first.Target.X, actualFirst.X, 8);
        Assert.Equal(first.Target.Y, actualFirst.Y, 8);
        Assert.Equal(second.Target.X, actualSecond.X, 8);
        Assert.Equal(second.Target.Y, actualSecond.Y, 8);
    }

    [Fact]
    public void ScaledSecondBendNeedsMorePointerTravel()
    {
        CubicPaintCurve first = ClassicCurveGeometry.BendOnce(
            new PaintPoint(0, 0),
            new PaintPoint(100, 0),
            new PaintCurveBend(0.3, new PaintPoint(30, 20)));
        PaintPoint origin = first.Evaluate(0.7);

        PaintCurveBend scaled = ClassicCurveGeometry.ScaleBendMovement(
            first,
            new PaintCurveBend(0.7, origin + new PaintPoint(0, 20)),
            0.35);

        Assert.Equal(origin.Y + 7, scaled.Target.Y, 8);
    }

    [Fact]
    public void DegenerateBendInputs_FallBackSafely()
    {
        PaintPoint same = new(12, 34);
        CubicPaintCurve curve = ClassicCurveGeometry.BendTwice(
            same,
            same,
            new PaintCurveBend(double.NaN, same),
            new PaintCurveBend(double.NaN, same));

        for (int index = 0; index <= 10; index++)
        {
            PaintPoint point = curve.Evaluate(index / 10.0);
            Assert.True(double.IsFinite(point.X));
            Assert.True(double.IsFinite(point.Y));
            Assert.Equal(same.X, point.X, 8);
            Assert.Equal(same.Y, point.Y, 8);
        }
    }

    [Fact]
    public void CurveSampler_BoundsSegmentCountAndKeepsEndpoints()
    {
        PaintPoint start = new(0, 0);
        PaintPoint end = new(100, 0);
        CubicPaintCurve curve = ClassicCurveGeometry.BendOnce(
            start,
            end,
            new PaintCurveBend(0.5, new PaintPoint(50, 40)));

        var points = ClassicCurveGeometry.Sample(curve, 2.0);

        Assert.Equal(start, points[0]);
        Assert.Equal(end, points[^1]);
        Assert.InRange(points.Count, 2, 4097);
    }
}

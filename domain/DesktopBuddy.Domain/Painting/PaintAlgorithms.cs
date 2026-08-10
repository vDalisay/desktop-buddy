using System;
using System.Collections.Generic;

namespace DesktopBuddy.Domain.Painting;

/// <summary>
/// Deterministic clean-room spray sampling shared by paint surfaces. The sampler returns points
/// in a unit disk; each caller owns its own pixel mapping, edge behavior, colour write and history.
/// </summary>
public static class SprayPattern
{
    // Tuning data, deliberately not player-facing. This produces about 14 points at diameter 24
    // and scales by envelope area so large sprays do not become visibly empty.
    private const double DotsPerPixelSquared = 0.03;
    private const int MinimumDotsPerPulse = 3;
    private const int MaximumDotsPerPulse = 512;

    public static int PointCountForDiameter(int diameter)
    {
        if (diameter <= 0) return MinimumDotsPerPulse;
        double radius = diameter / 2.0;
        double area = Math.PI * radius * radius;
        return Math.Clamp((int)Math.Ceiling(area * DotsPerPixelSquared), MinimumDotsPerPulse, MaximumDotsPerPulse);
    }

    public static PaintPoint[] SampleUnitDisk(ulong seed, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var points = new PaintPoint[count];
        var rng = new SplitMix64(seed);
        for (int index = 0; index < count; index++)
        {
            double angle = Math.Tau * rng.NextUnit();
            double radius = Math.Sqrt(rng.NextUnit());
            points[index] = new PaintPoint(Math.Cos(angle) * radius, Math.Sin(angle) * radius);
        }
        return points;
    }

    private struct SplitMix64
    {
        private ulong _state;
        public SplitMix64(ulong seed) => _state = seed;

        public double NextUnit()
        {
            ulong value = NextUInt64();
            // 53 stable high bits mapped into [0,1), independent of System.Random implementation.
            return (value >> 11) * (1.0 / 9007199254740992.0);
        }

        private ulong NextUInt64()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}

public readonly record struct PaintCurveBend(double T, PaintPoint Target);

public readonly record struct CubicPaintCurve(PaintPoint Start, PaintPoint Control1, PaintPoint Control2, PaintPoint End)
{
    public PaintPoint Evaluate(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        double oneMinus = 1.0 - t;
        double b0 = oneMinus * oneMinus * oneMinus;
        double b1 = 3.0 * oneMinus * oneMinus * t;
        double b2 = 3.0 * oneMinus * t * t;
        double b3 = t * t * t;
        return (Start * b0) + (Control1 * b1) + (Control2 * b2) + (End * b3);
    }
}

/// <summary>
/// Geometry-only helper for a classic multi-stage curved-line interaction. It knows nothing about
/// Godot, UV seams, clipping, paint history or pixels; surfaces sample and rasterise the curve.
/// </summary>
public static class ClassicCurveGeometry
{
    private const double MinimumT = 0.05;
    private const double MaximumT = 0.95;
    private const double SingularEpsilon = 1e-8;

    public static CubicPaintCurve Straight(PaintPoint start, PaintPoint end)
    {
        PaintPoint delta = end - start;
        return new CubicPaintCurve(start, start + (delta * (1.0 / 3.0)), start + (delta * (2.0 / 3.0)), end);
    }

    public static double ClosestParameter(CubicPaintCurve curve, PaintPoint point, int samples = 64)
    {
        samples = Math.Clamp(samples, 8, 512);
        double bestT = 0.5;
        double bestDistanceSquared = double.PositiveInfinity;
        for (int index = 0; index <= samples; index++)
        {
            double t = index / (double)samples;
            PaintPoint candidate = curve.Evaluate(t);
            double dx = candidate.X - point.X;
            double dy = candidate.Y - point.Y;
            double distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                bestT = t;
            }
        }
        return ClampBendParameter(bestT);
    }

    public static CubicPaintCurve BendOnce(PaintPoint start, PaintPoint end, PaintCurveBend bend)
    {
        CubicPaintCurve baseline = Straight(start, end);
        double t = ClampBendParameter(bend.T);
        Coefficients(t, out double b0, out double b1, out double b2, out double b3);
        if (Math.Abs(b1) < SingularEpsilon)
            return baseline;

        PaintPoint fixedContribution = (start * b0) + (baseline.Control2 * b2) + (end * b3);
        PaintPoint control1 = (bend.Target - fixedContribution) * (1.0 / b1);
        return new CubicPaintCurve(start, control1, baseline.Control2, end);
    }

    public static CubicPaintCurve BendTwice(
        PaintPoint start,
        PaintPoint end,
        PaintCurveBend first,
        PaintCurveBend second)
    {
        double t1 = ClampBendParameter(first.T);
        double t2 = ClampBendParameter(second.T);
        Coefficients(t1, out double a0, out double a1, out double a2, out double a3);
        Coefficients(t2, out double b0, out double b1, out double b2, out double b3);
        double determinant = (a1 * b2) - (b1 * a2);
        if (Math.Abs(determinant) < SingularEpsilon)
            return BendOnce(start, end, first);

        PaintPoint rhs1 = first.Target - ((start * a0) + (end * a3));
        PaintPoint rhs2 = second.Target - ((start * b0) + (end * b3));
        PaintPoint control1 = ((rhs1 * b2) - (rhs2 * a2)) * (1.0 / determinant);
        PaintPoint control2 = ((rhs2 * a1) - (rhs1 * b1)) * (1.0 / determinant);
        return new CubicPaintCurve(start, control1, control2, end);
    }

    public static IReadOnlyList<PaintPoint> Sample(CubicPaintCurve curve, double maximumSegmentLength)
    {
        if (!double.IsFinite(maximumSegmentLength) || maximumSegmentLength <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumSegmentLength));

        double controlLength = (curve.Control1 - curve.Start).Length +
                               (curve.Control2 - curve.Control1).Length +
                               (curve.End - curve.Control2).Length;
        int segments = Math.Clamp((int)Math.Ceiling(controlLength / maximumSegmentLength), 1, 4096);
        var points = new PaintPoint[segments + 1];
        for (int index = 0; index <= segments; index++)
            points[index] = curve.Evaluate(index / (double)segments);
        return points;
    }

    private static double ClampBendParameter(double t) =>
        double.IsFinite(t) ? Math.Clamp(t, MinimumT, MaximumT) : 0.5;

    private static void Coefficients(double t, out double b0, out double b1, out double b2, out double b3)
    {
        double oneMinus = 1.0 - t;
        b0 = oneMinus * oneMinus * oneMinus;
        b1 = 3.0 * oneMinus * oneMinus * t;
        b2 = 3.0 * oneMinus * t * t;
        b3 = t * t * t;
    }
}

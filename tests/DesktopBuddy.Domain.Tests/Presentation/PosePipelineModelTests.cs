using System;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class PoseModeArbiterTests
{
    private static readonly PoseModeInputs Calm = new(
        Unconscious: false,
        RecoveryActive: false,
        GrabActive: false,
        ReactionActive: false,
        StableStanding: true,
        TicksSinceImpact: int.MaxValue);

    [Fact]
    public void CalmStableState_AllowsPerformance() =>
        Assert.Equal(PresentationPoseMode.Performance, PoseModeArbiter.Evaluate(Calm, 60));

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void AnyForcingFlag_ForcesTracking(
        bool unconscious, bool recovery, bool grab, bool reaction)
    {
        PoseModeInputs inputs = Calm with
        {
            Unconscious = unconscious,
            RecoveryActive = recovery,
            GrabActive = grab,
            ReactionActive = reaction,
        };
        Assert.Equal(PresentationPoseMode.Tracking, PoseModeArbiter.Evaluate(inputs, 60));
    }

    [Fact]
    public void UnstableStanding_ForcesTracking() =>
        Assert.Equal(
            PresentationPoseMode.Tracking,
            PoseModeArbiter.Evaluate(Calm with { StableStanding = false }, 60));

    [Theory]
    [InlineData(0, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(61, false)]
    public void PostImpactCooldown_BoundaryIsExclusive(int ticksSinceImpact, bool expectTracking)
    {
        PresentationPoseMode mode = PoseModeArbiter.Evaluate(
            Calm with { TicksSinceImpact = ticksSinceImpact }, 60);
        Assert.Equal(
            expectTracking ? PresentationPoseMode.Tracking : PresentationPoseMode.Performance,
            mode);
    }

    [Fact]
    public void ZeroCooldown_NeverForcesTrackingFromImpact() =>
        Assert.Equal(
            PresentationPoseMode.Performance,
            PoseModeArbiter.Evaluate(Calm with { TicksSinceImpact = 0 }, 0));

    [Fact]
    public void NegativeCooldown_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PoseModeArbiter.Evaluate(Calm, -1));
}

public sealed class PerformanceBlendTests
{
    [Theory]
    [InlineData(0.0f)]
    [InlineData(-0.2f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidDuration_Throws(float seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new PerformanceBlend(seconds));

    [Fact]
    public void EasesToFullOverConfiguredSeconds()
    {
        var blend = new PerformanceBlend(0.2f);
        Assert.Equal(0.5f, blend.Update(0.1, PresentationPoseMode.Performance), 3);
        Assert.Equal(1.0f, blend.Update(0.1, PresentationPoseMode.Performance), 3);
        Assert.Equal(1.0f, blend.Update(1.0, PresentationPoseMode.Performance), 3);
    }

    [Fact]
    public void TrackingSnapsInstantlyToZero()
    {
        var blend = new PerformanceBlend(0.2f);
        blend.Update(1.0, PresentationPoseMode.Performance);
        Assert.Equal(0.0f, blend.Update(0.0001, PresentationPoseMode.Tracking));
        Assert.Equal(0.0f, blend.Weight);
    }

    [Fact]
    public void ZeroOrNegativeDelta_DoesNotAdvance()
    {
        var blend = new PerformanceBlend(0.2f);
        Assert.Equal(0.0f, blend.Update(0.0, PresentationPoseMode.Performance));
        Assert.Equal(0.0f, blend.Update(-0.5, PresentationPoseMode.Performance));
    }

    [Fact]
    public void Reset_ReturnsToZero()
    {
        var blend = new PerformanceBlend(0.2f);
        blend.Update(1.0, PresentationPoseMode.Performance);
        blend.Reset();
        Assert.Equal(0.0f, blend.Weight);
    }
}

public sealed class BoundedOffsetTests
{
    [Fact]
    public void OffsetInsideCap_IsUnchanged()
    {
        (float x, float y, float z) = BoundedOffset.Clamp(1.0f, 2.0f, 2.0f, 4.0f);
        Assert.Equal(1.0f, x);
        Assert.Equal(2.0f, y);
        Assert.Equal(2.0f, z);
    }

    [Fact]
    public void OffsetOutsideCap_IsScaledToCapMagnitude()
    {
        (float x, float y, float z) = BoundedOffset.Clamp(3.0f, 4.0f, 0.0f, 2.5f);
        float magnitude = MathF.Sqrt((x * x) + (y * y) + (z * z));
        Assert.Equal(2.5f, magnitude, 3);
        Assert.Equal(x / 3.0f, y / 4.0f, 3);
    }

    [Fact]
    public void ZeroCap_ClampsToZero()
    {
        (float x, float y, float z) = BoundedOffset.Clamp(3.0f, 4.0f, 5.0f, 0.0f);
        Assert.Equal(0.0f, x);
        Assert.Equal(0.0f, y);
        Assert.Equal(0.0f, z);
    }

    [Theory]
    [InlineData(float.NaN, 0.0f, 0.0f)]
    [InlineData(0.0f, float.PositiveInfinity, 0.0f)]
    [InlineData(0.0f, 0.0f, float.NegativeInfinity)]
    public void NonFiniteOffset_CollapsesToZero(float x, float y, float z)
    {
        (float cx, float cy, float cz) = BoundedOffset.Clamp(x, y, z, 4.0f);
        Assert.Equal(0.0f, cx);
        Assert.Equal(0.0f, cy);
        Assert.Equal(0.0f, cz);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(-1.0f)]
    public void InvalidCap_Throws(float cap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BoundedOffset.Clamp(1.0f, 0.0f, 0.0f, cap));
}

public sealed class ExpressionTuningDataTests
{
    private static readonly ExpressionTuningData Accepted = new(
        PerformanceBlendSeconds: 0.2f,
        PostImpactCooldownTicks: 60,
        OffsetCapRadiusFraction: 0.5f,
        FacingYawDegrees: 30.0f,
        FacingTurnSeconds: 0.5f,
        FacingWalkCommitTicks: 36,
        FacingWalkDeadband: 0.05f,
        FacingIdleFlipMinimumTicks: 720,
        FacingIdleFlipMaximumTicks: 1920,
        LookConeYawDegrees: 28.0f,
        LookConePitchDegrees: 18.0f,
        LookEaseSeconds: 0.25f,
        LookGazeDepthPixels: 120.0f,
        LookEngagementRangePixels: 220.0f,
        LookImpactMemoryTicks: 240,
        LookGlanceIntervalMinimumTicks: 480,
        LookGlanceIntervalMaximumTicks: 1200,
        LookGlanceHoldMinimumTicks: 72,
        LookGlanceHoldMaximumTicks: 168,
        LookPupilQuantizationSteps: 4,
        BlinkIntervalMinimumTicks: 240,
        BlinkIntervalMaximumTicks: 720,
        BlinkClosedTicks: 14,
        ChewCycleTicks: 42);

    [Fact]
    public void AcceptedDefaults_Pass() => Assert.Empty(Accepted.Validate());

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-0.2f)]
    [InlineData(float.NaN)]
    [InlineData(2.5f)]
    public void InvalidBlendSeconds_Fails(float seconds) =>
        Assert.Single(
            (Accepted with { PerformanceBlendSeconds = seconds }).Validate(),
            error => error.Contains("blend seconds"));

    [Theory]
    [InlineData(-1)]
    [InlineData(1201)]
    public void InvalidCooldownTicks_Fails(int ticks) =>
        Assert.Single(
            (Accepted with { PostImpactCooldownTicks = ticks }).Validate(),
            error => error.Contains("cooldown ticks"));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(0.51f)]
    [InlineData(float.NaN)]
    public void InvalidOffsetCapFraction_Fails(float fraction) =>
        Assert.Single(
            (Accepted with { OffsetCapRadiusFraction = fraction }).Validate(),
            error => error.Contains("offset cap"));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(46.0f)]
    [InlineData(float.NaN)]
    public void InvalidFacingYaw_Fails(float degrees) =>
        Assert.Single(
            (Accepted with { FacingYawDegrees = degrees }).Validate(),
            error => error.Contains("facing yaw"));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(2.5f)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidFacingTurnSeconds_Fails(float seconds) =>
        Assert.Single(
            (Accepted with { FacingTurnSeconds = seconds }).Validate(),
            error => error.Contains("facing turn"));

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void InvalidFacingCommitTicks_Fails(int ticks) =>
        Assert.Single(
            (Accepted with { FacingWalkCommitTicks = ticks }).Validate(),
            error => error.Contains("commit ticks"));

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.0f)]
    [InlineData(float.NaN)]
    public void InvalidFacingDeadband_Fails(float deadband) =>
        Assert.Single(
            (Accepted with { FacingWalkDeadband = deadband }).Validate(),
            error => error.Contains("deadband"));

    [Theory]
    [InlineData(0, 1920)]
    [InlineData(720, 720)]
    [InlineData(720, 14401)]
    public void InvalidFacingIdleFlipRange_Fails(int minimum, int maximum) =>
        Assert.Single(
            (Accepted with
            {
                FacingIdleFlipMinimumTicks = minimum,
                FacingIdleFlipMaximumTicks = maximum,
            }).Validate(),
            error => error.Contains("idle flip"));

    [Fact]
    public void ToFacingParameters_ProjectsTheFacingSubset()
    {
        FacingParameters parameters = Accepted.ToFacingParameters();
        Assert.Equal(30.0f, parameters.YawDegrees);
        Assert.Equal(0.5f, parameters.TurnSeconds);
        Assert.Equal(36, parameters.WalkCommitTicks);
        Assert.Equal(0.05f, parameters.WalkDeadband);
        Assert.Equal(720, parameters.IdleFlipMinimumTicks);
        Assert.Equal(1920, parameters.IdleFlipMaximumTicks);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(61.0f)]
    [InlineData(float.NaN)]
    public void InvalidLookConeYaw_Fails(float degrees) =>
        Assert.Single(
            (Accepted with { LookConeYawDegrees = degrees }).Validate(),
            error => error.Contains("look cone yaw"));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(46.0f)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidLookConePitch_Fails(float degrees) =>
        Assert.Single(
            (Accepted with { LookConePitchDegrees = degrees }).Validate(),
            error => error.Contains("look cone pitch"));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    public void InvalidLookEaseSeconds_Fails(float seconds) =>
        Assert.Single(
            (Accepted with { LookEaseSeconds = seconds }).Validate(),
            error => error.Contains("look ease"));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-10.0f)]
    [InlineData(float.PositiveInfinity)]
    public void InvalidLookGazeDepth_Fails(float pixels) =>
        Assert.Single(
            (Accepted with { LookGazeDepthPixels = pixels }).Validate(),
            error => error.Contains("gaze depth"));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(float.NaN)]
    public void InvalidLookEngagementRange_Fails(float pixels) =>
        Assert.Single(
            (Accepted with { LookEngagementRangePixels = pixels }).Validate(),
            error => error.Contains("engagement range"));

    [Theory]
    [InlineData(-1)]
    [InlineData(601)]
    public void InvalidLookImpactMemory_Fails(int ticks) =>
        Assert.Single(
            (Accepted with { LookImpactMemoryTicks = ticks }).Validate(),
            error => error.Contains("impact memory"));

    [Theory]
    [InlineData(0, 1200)]
    [InlineData(480, 480)]
    [InlineData(480, 14401)]
    public void InvalidLookGlanceIntervalRange_Fails(int minimum, int maximum) =>
        Assert.Single(
            (Accepted with
            {
                LookGlanceIntervalMinimumTicks = minimum,
                LookGlanceIntervalMaximumTicks = maximum,
            }).Validate(),
            error => error.Contains("glance interval"));

    [Theory]
    [InlineData(0, 168)]
    [InlineData(72, 72)]
    [InlineData(72, 601)]
    public void InvalidLookGlanceHoldRange_Fails(int minimum, int maximum) =>
        Assert.Single(
            (Accepted with
            {
                LookGlanceHoldMinimumTicks = minimum,
                LookGlanceHoldMaximumTicks = maximum,
            }).Validate(),
            error => error.Contains("glance hold"));

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void InvalidPupilQuantizationSteps_Fails(int steps) =>
        Assert.Single(
            (Accepted with { LookPupilQuantizationSteps = steps }).Validate(),
            error => error.Contains("pupil quantization"));

    [Fact]
    public void ToLookAtParameters_ProjectsTheLookSubset()
    {
        LookAtParameters parameters = Accepted.ToLookAtParameters();
        Assert.Equal(28.0f, parameters.ConeYawDegrees);
        Assert.Equal(18.0f, parameters.ConePitchDegrees);
        Assert.Equal(0.25f, parameters.EaseSeconds);
        Assert.Equal(120.0f, parameters.GazeDepth);
        Assert.Equal(220.0f, parameters.EngagementRange);
        Assert.Equal(240, parameters.ImpactMemoryTicks);
        Assert.Equal(480, parameters.GlanceIntervalMinimumTicks);
        Assert.Equal(1200, parameters.GlanceIntervalMaximumTicks);
        Assert.Equal(72, parameters.GlanceHoldMinimumTicks);
        Assert.Equal(168, parameters.GlanceHoldMaximumTicks);
        Assert.Equal(4, parameters.PupilQuantizationSteps);
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(240, 240)]
    [InlineData(240, 2401)]
    public void InvalidBlinkInterval_Fails(int minimum, int maximum) =>
        Assert.Single(
            (Accepted with
            {
                BlinkIntervalMinimumTicks = minimum,
                BlinkIntervalMaximumTicks = maximum,
            }).Validate(),
            error => error.Contains("blink interval"));

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void InvalidBlinkClosedTicks_Fails(int closed) =>
        Assert.Single(
            (Accepted with { BlinkClosedTicks = closed }).Validate(),
            error => error.Contains("blink closed"));

    [Theory]
    [InlineData(11)]
    [InlineData(241)]
    public void InvalidChewCycleTicks_Fails(int chew) =>
        Assert.Single(
            (Accepted with { ChewCycleTicks = chew }).Validate(),
            error => error.Contains("chew cycle"));

    [Fact]
    public void ToBlinkParameters_ProjectsTheBlinkSubset()
    {
        BlinkParameters parameters = Accepted.ToBlinkParameters();
        Assert.Equal(240, parameters.IntervalMinimumTicks);
        Assert.Equal(720, parameters.IntervalMaximumTicks);
        Assert.Equal(14, parameters.ClosedTicks);
    }
}

using System;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class ActivitySelectorTests
{
    private static readonly ActivityParameters Parameters = new(
        WalkSpeedThreshold: 8.0f,
        WalkCyclePixelsPerCycle: 48.0f,
        JumpAnticipationSeconds: 0.15f,
        WaveSeconds: 1.2f);

    private static ActivitySelector NewSelector() => new(Parameters);

    private static readonly ActivityInputs CalmIdle = new(true, 0.0f, false);
    private static readonly ActivityInputs Walking = new(true, 48.0f, false);
    private const double Dt = 1.0 / 120.0;

    [Fact]
    public void CalmPerformance_SelectsIdleBreathe()
    {
        ActivitySelector selector = NewSelector();
        Assert.Equal(ActivityId.IdleBreathe, selector.Update(CalmIdle, Dt));
    }

    [Fact]
    public void TrackingSuppression_SelectsNone()
    {
        ActivitySelector selector = NewSelector();
        selector.Update(Walking, Dt);
        Assert.Equal(ActivityId.None, selector.Update(new ActivityInputs(false, 48.0f, false), Dt));
    }

    [Fact]
    public void SpeedAboveThreshold_SelectsWalkCycle_AndPhaseMatchesTravel()
    {
        ActivitySelector selector = NewSelector();
        // 48 px/s over one second at 120 Hz = one full cycle of 48 px.
        for (int tick = 0; tick < 120; tick++)
        {
            Assert.Equal(ActivityId.WalkCycle, selector.Update(Walking, Dt));
        }

        // Phase wrapped back to ~0 after exactly one cycle.
        Assert.True(selector.WalkPhase < 0.02f || selector.WalkPhase > 0.98f,
            $"phase={selector.WalkPhase}");
    }

    [Fact]
    public void PhaseRate_IsProportionalToSpeed()
    {
        ActivitySelector slow = NewSelector();
        ActivitySelector fast = NewSelector();
        for (int tick = 0; tick < 60; tick++)
        {
            slow.Update(new ActivityInputs(true, 24.0f, false), Dt);
            fast.Update(new ActivityInputs(true, 96.0f, false), Dt);
        }

        Assert.Equal(slow.WalkPhase * 4.0f, fast.WalkPhase, 3);
    }

    [Fact]
    public void ZeroSpeed_FreezesThePhase()
    {
        ActivitySelector selector = NewSelector();
        for (int tick = 0; tick < 30; tick++)
        {
            selector.Update(Walking, Dt);
        }

        float frozen = selector.WalkPhase;
        for (int tick = 0; tick < 60; tick++)
        {
            selector.Update(CalmIdle, Dt);
        }

        Assert.Equal(frozen, selector.WalkPhase);
    }

    [Fact]
    public void EatRequest_OutranksEverything_ForItsDuration()
    {
        ActivitySelector selector = NewSelector();
        selector.RequestEat(0.5);
        selector.RequestWave();
        Assert.Equal(ActivityId.Eat, selector.Update(Walking, Dt));
        // After the eat window the wave (still pending) takes over.
        ActivityId after = ActivityId.None;
        for (int tick = 0; tick < 70; tick++)
        {
            after = selector.Update(Walking, Dt);
        }

        Assert.Equal(ActivityId.Wave, after);
    }

    [Fact]
    public void Wave_IsOneShot_ThenAmbientResumes()
    {
        ActivitySelector selector = NewSelector();
        selector.RequestWave();
        Assert.Equal(ActivityId.Wave, selector.Update(CalmIdle, Dt));
        for (int tick = 0; tick < 150; tick++)
        {
            selector.Update(CalmIdle, Dt);
        }

        Assert.Equal(ActivityId.IdleBreathe, selector.Current);
    }

    [Fact]
    public void JumpRequest_OpensAnticipationWindow_ThenExpires()
    {
        ActivitySelector selector = NewSelector();
        Assert.Equal(ActivityId.JumpAnticipation,
            selector.Update(new ActivityInputs(true, 0.0f, true), Dt));
        for (int tick = 0; tick < 20; tick++)
        {
            selector.Update(CalmIdle, Dt);
        }

        Assert.Equal(ActivityId.IdleBreathe, selector.Current);
    }

    [Fact]
    public void TrackingCut_KeepsCountingDownBehaviorRequests()
    {
        ActivitySelector selector = NewSelector();
        selector.RequestEat(0.2);
        var tracking = new ActivityInputs(false, 0.0f, false);
        for (int tick = 0; tick < 36; tick++)
        {
            Assert.Equal(ActivityId.None, selector.Update(tracking, Dt));
        }

        // 0.3 s of tracking outlived the 0.2 s eat: it must not resume.
        Assert.Equal(ActivityId.IdleBreathe, selector.Update(CalmIdle, Dt));
    }

    [Fact]
    public void CancelRequests_DropsPendingActivities()
    {
        ActivitySelector selector = NewSelector();
        selector.RequestEat(5.0);
        selector.CancelRequests();
        Assert.Equal(ActivityId.IdleBreathe, selector.Update(CalmIdle, Dt));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void InvalidEatDuration_Throws(double seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewSelector().RequestEat(seconds));

    [Fact]
    public void InvalidParameters_Throw() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ActivitySelector(Parameters with { WalkCyclePixelsPerCycle = 0.0f }));
}

public sealed class ActivityTuningDataTests
{
    private static readonly ActivityTuningData Accepted = new(
        WalkSpeedThreshold: 8.0f,
        WalkCyclePixelsPerCycle: 48.0f,
        JumpAnticipationSeconds: 0.15f,
        WaveSeconds: 1.2f,
        EatDefaultSeconds: 2.0f,
        BreatheSeconds: 3.2f,
        BreatheAmplitude: 1.2f,
        WalkBobAmplitude: 1.5f,
        WaveAmplitude: 3.0f,
        ChewAmplitude: 1.0f,
        JumpSquashAmplitude: 2.5f,
        RefuseYawDegrees: 30.0f);

    [Fact]
    public void AcceptedDefaults_Pass() => Assert.Empty(Accepted.Validate());

    [Theory]
    [InlineData(0.0f)]
    [InlineData(7.0f)]
    [InlineData(float.NaN)]
    public void AmplitudeOutsideSubtleBound_Fails(float amplitude) =>
        Assert.Single(
            (Accepted with { BreatheAmplitude = amplitude }).Validate(),
            error => error.Contains("breathe amplitude"));

    /// <summary>
    /// Refusal rotates around the neck's vertical axis. The owner bounded the first extreme
    /// to the natural over-the-shoulder range of 20–30 degrees.
    /// </summary>
    [Theory]
    [InlineData(0.0f)]
    [InlineData(19.0f)]
    [InlineData(31.0f)]
    [InlineData(float.NaN)]
    public void RefuseYawOutsideOwnerBound_Fails(float yawDegrees) =>
        Assert.Single(
            (Accepted with { RefuseYawDegrees = yawDegrees }).Validate(),
            error => error.Contains("refuse yaw"));

    [Theory]
    [InlineData(20.0f)]
    [InlineData(30.0f)]
    public void RefuseYawAtOwnerBounds_Passes(float yawDegrees) =>
        Assert.Empty((Accepted with { RefuseYawDegrees = yawDegrees }).Validate());

    [Theory]
    [InlineData(0.0f)]
    [InlineData(401.0f)]
    public void InvalidWalkCyclePixels_Fails(float pixels) =>
        Assert.Single(
            (Accepted with { WalkCyclePixelsPerCycle = pixels }).Validate(),
            error => error.Contains("walk cycle pixels"));

    [Fact]
    public void ToActivityParameters_ProjectsTheSelectorSubset()
    {
        ActivityParameters parameters = Accepted.ToActivityParameters();
        Assert.Equal(8.0f, parameters.WalkSpeedThreshold);
        Assert.Equal(48.0f, parameters.WalkCyclePixelsPerCycle);
        Assert.Equal(0.15f, parameters.JumpAnticipationSeconds);
        Assert.Equal(1.2f, parameters.WaveSeconds);
    }
}

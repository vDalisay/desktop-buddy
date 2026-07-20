using System;
using DesktopBuddy.Domain.Autonomy;
using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class LookAtModelTests
{
    private const double Frame = 1.0 / 120.0;

    private static readonly LookAtParameters Parameters = new(
        ConeYawDegrees: 28.0f,
        ConePitchDegrees: 18.0f,
        EaseSeconds: 0.25f,
        GazeDepth: 120.0f,
        EngagementRange: 220.0f,
        ImpactMemoryTicks: 240,
        GlanceIntervalMinimumTicks: 480,
        GlanceIntervalMaximumTicks: 1200,
        GlanceHoldMinimumTicks: 72,
        GlanceHoldMaximumTicks: 168,
        PupilQuantizationSteps: 4);

    /// <summary>Head at the origin; every input point is therefore its own world delta.</summary>
    private static readonly LookAtInputs Idle = new(
        InteractionEngaged: false,
        CursorX: 0.0f, CursorY: 0.0f,
        ItemTargetValid: false,
        ItemX: 0.0f, ItemY: 0.0f,
        TicksSinceImpact: int.MaxValue,
        ImpactX: 0.0f, ImpactY: 0.0f,
        FaceSuppressed: false,
        HeadX: 0.0f, HeadY: 0.0f);

    private static LookAtModel NewModel(params int[] scripted) =>
        new(new ScriptedRandomSource(scripted), Parameters);

    private static LookAtModel SeededModel(ulong seed) =>
        new(new SeededRandomSource(seed), Parameters);

    /// <summary>The scenario's oracle math, duplicated here independently of the model.</summary>
    private static float ExpectedAngleDegrees(float delta) =>
        MathF.Atan2(delta, 120.0f) * (180.0f / MathF.PI);

    /// <summary>Runs the model to its eased steady state against a fixed input.</summary>
    private static LookAtAngles Settle(LookAtModel model, in LookAtInputs inputs, int frames = 120)
    {
        LookAtAngles angles = default;
        for (int frame = 0; frame < frames; frame++)
        {
            angles = model.Update(inputs, 1, Frame);
        }

        return angles;
    }

    [Fact]
    public void StartsAtRest()
    {
        LookAtModel model = NewModel();
        LookAtAngles angles = model.Update(Idle, 1, Frame);
        Assert.Equal(LookAtSource.Rest, model.CurrentSource);
        Assert.Equal(0.0f, angles.YawDegrees);
        Assert.Equal(0.0f, angles.PitchDegrees);
    }

    [Fact]
    public void EngagedCursorInRange_WinsAndAimsAtTheCursor()
    {
        LookAtModel model = NewModel();
        LookAtInputs inputs = Idle with
        {
            InteractionEngaged = true,
            CursorX = 60.0f,
            CursorY = 20.0f,
        };

        LookAtAngles angles = Settle(model, inputs);
        Assert.Equal(LookAtSource.Cursor, model.CurrentSource);
        Assert.Equal(ExpectedAngleDegrees(60.0f), angles.YawDegrees, 3);
        Assert.Equal(ExpectedAngleDegrees(20.0f), angles.PitchDegrees, 3);
    }

    [Fact]
    public void EngagedCursorBeyondRange_DoesNotWin()
    {
        LookAtModel model = NewModel();
        LookAtInputs inputs = Idle with
        {
            InteractionEngaged = true,
            CursorX = 400.0f,
            CursorY = 0.0f,
        };

        model.Update(inputs, 1, Frame);
        Assert.NotEqual(LookAtSource.Cursor, model.CurrentSource);
    }

    [Fact]
    public void UnengagedCursor_IsNeverTracked()
    {
        // Owner-resolved scope: plain idle ignores the cursor entirely.
        LookAtModel model = NewModel();
        LookAtInputs inputs = Idle with { CursorX = 40.0f, CursorY = 10.0f };
        Settle(model, inputs, 60);
        Assert.NotEqual(LookAtSource.Cursor, model.CurrentSource);
        Assert.Equal(0.0f, model.CurrentYawDegrees, 3);
    }

    [Fact]
    public void ItemTarget_OutranksImpactAndAmbient()
    {
        LookAtModel model = NewModel();
        LookAtInputs inputs = Idle with
        {
            ItemTargetValid = true,
            ItemX = -30.0f,
            ItemY = 12.0f,
            TicksSinceImpact = 0,
            ImpactX = 90.0f,
        };

        LookAtAngles angles = Settle(model, inputs);
        Assert.Equal(LookAtSource.Item, model.CurrentSource);
        Assert.Equal(ExpectedAngleDegrees(-30.0f), angles.YawDegrees, 3);
    }

    [Fact]
    public void CursorOutranksItem()
    {
        LookAtModel model = NewModel();
        LookAtInputs inputs = Idle with
        {
            InteractionEngaged = true,
            CursorX = 50.0f,
            ItemTargetValid = true,
            ItemX = -50.0f,
        };

        LookAtAngles angles = Settle(model, inputs);
        Assert.Equal(LookAtSource.Cursor, model.CurrentSource);
        Assert.True(angles.YawDegrees > 0.0f);
    }

    [Fact]
    public void ImpactMemory_WatchesThePointThenDecays()
    {
        LookAtModel model = NewModel();
        LookAtInputs watching = Idle with { TicksSinceImpact = 0, ImpactX = 45.0f, ImpactY = -15.0f };
        LookAtAngles angles = Settle(model, watching);
        Assert.Equal(LookAtSource.Impact, model.CurrentSource);
        Assert.Equal(ExpectedAngleDegrees(45.0f), angles.YawDegrees, 3);

        // Past the memory window the impact stops being interesting.
        model.Update(watching with { TicksSinceImpact = 240 }, 1, Frame);
        Assert.NotEqual(LookAtSource.Impact, model.CurrentSource);
    }

    [Fact]
    public void ExtremeTargets_ClampToTheCone()
    {
        LookAtModel model = NewModel();
        LookAtInputs inputs = Idle with
        {
            InteractionEngaged = true,
            CursorX = 200.0f,
            CursorY = 90.0f,
        };

        LookAtAngles angles = Settle(model, inputs);
        Assert.Equal(28.0f, angles.YawDegrees, 3);
        Assert.Equal(18.0f, angles.PitchDegrees, 3);
    }

    [Fact]
    public void EveryEasedSample_StaysInsideTheCone()
    {
        LookAtModel model = SeededModel(7);
        // Both extremes sit inside the engagement range but far outside the cone.
        LookAtInputs right = Idle with { InteractionEngaged = true, CursorX = 150.0f, CursorY = 100.0f };
        LookAtInputs left = right with { CursorX = -150.0f, CursorY = -100.0f };
        for (int frame = 0; frame < 600; frame++)
        {
            // Alternate extremes every 30 frames so the ease is constantly re-acquiring.
            LookAtAngles angles = model.Update(
                (frame / 30) % 2 == 0 ? right : left, 1, Frame);
            Assert.True(MathF.Abs(angles.YawDegrees) <= 28.0001f, $"yaw left cone: {angles.YawDegrees}");
            Assert.True(MathF.Abs(angles.PitchDegrees) <= 18.0001f, $"pitch left cone: {angles.PitchDegrees}");
        }
    }

    [Fact]
    public void SwitchingSides_CrossesZeroWithoutOvershoot()
    {
        LookAtModel model = NewModel();
        LookAtInputs right = Idle with { InteractionEngaged = true, CursorX = 200.0f };
        Settle(model, right);
        Assert.Equal(28.0f, model.CurrentYawDegrees, 3);

        LookAtInputs left = right with { CursorX = -200.0f };
        bool crossedZero = false;
        float previous = model.CurrentYawDegrees;
        for (int frame = 0; frame < 120; frame++)
        {
            float yaw = model.Update(left, 1, Frame).YawDegrees;
            Assert.True(yaw <= previous + 0.0001f, $"yaw regressed at frame {frame}");
            Assert.True(MathF.Abs(yaw) <= 28.0001f, $"yaw overshot at frame {frame}: {yaw}");
            crossedZero |= yaw < 0.0f && previous >= 0.0f;
            previous = yaw;
        }

        Assert.True(crossedZero, "turn never crossed zero");
        Assert.Equal(-28.0f, model.CurrentYawDegrees, 3);
    }

    [Fact]
    public void AmbientGlance_FiresOnTheSeededScheduleAndReturnsToRest()
    {
        // Scripted stream: interval 480, hold 72, glance yaw sample, glance pitch sample,
        // then the interval that follows the glance.
        LookAtModel model = NewModel(480, 72, 1000, -1000, 480);
        model.Update(Idle, 479, 479.0 / 120.0);
        Assert.Equal(LookAtSource.Rest, model.CurrentSource);

        model.Update(Idle, 1, Frame);
        Assert.Equal(LookAtSource.Glance, model.CurrentSource);
        LookAtAngles angles = Settle(model, Idle, 60);
        Assert.Equal(28.0f, angles.YawDegrees, 3);
        Assert.Equal(-18.0f, angles.PitchDegrees, 3);

        // The hold expires and the gaze eases back to rest.
        model.Update(Idle, 72, 72.0 / 120.0);
        Assert.Equal(LookAtSource.Rest, model.CurrentSource);
        Settle(model, Idle, 60);
        Assert.Equal(0.0f, model.CurrentYawDegrees, 3);
    }

    [Fact]
    public void AmbientGlance_IsDeterministicPerSeed()
    {
        var first = new float[64];
        var second = new float[64];
        LookAtModel a = SeededModel(1);
        LookAtModel b = SeededModel(1);
        LookAtModel other = SeededModel(2);
        var otherSamples = new float[64];
        for (int index = 0; index < first.Length; index++)
        {
            first[index] = a.Update(Idle, 60, 0.5).YawDegrees;
            second[index] = b.Update(Idle, 60, 0.5).YawDegrees;
            otherSamples[index] = other.Update(Idle, 60, 0.5).YawDegrees;
        }

        Assert.Equal(first, second);
        Assert.NotEqual(first, otherSamples);
    }

    [Fact]
    public void HigherPriorityTarget_ReArmsTheGlanceTimer()
    {
        LookAtModel model = NewModel(480, 72, 0, 0, 480, 72, 0, 0);
        model.Update(Idle, 400, 400.0 / 120.0);
        model.Update(Idle with { ItemTargetValid = true }, 10, 10.0 / 120.0);
        // Ambient again: the interval re-arms from scratch, so 400 more ticks do not glance.
        model.Update(Idle, 400, 400.0 / 120.0);
        Assert.Equal(LookAtSource.Rest, model.CurrentSource);
    }

    [Fact]
    public void SuppressedFace_EasesBackToRest()
    {
        LookAtModel model = NewModel();
        LookAtInputs watching = Idle with { InteractionEngaged = true, CursorX = 200.0f };
        Settle(model, watching);
        Assert.Equal(28.0f, model.CurrentYawDegrees, 3);

        LookAtInputs suppressed = watching with { FaceSuppressed = true };
        // A single frame must not teleport: the return is eased, not a cut.
        float afterOneFrame = model.Update(suppressed, 1, Frame).YawDegrees;
        Assert.Equal(LookAtSource.Rest, model.CurrentSource);
        Assert.True(afterOneFrame > 27.0f, $"suppression cut instead of easing: {afterOneFrame}");

        Settle(model, suppressed);
        Assert.Equal(0.0f, model.CurrentYawDegrees, 3);
    }

    [Fact]
    public void PupilOffset_IsNormalizedAndQuantized()
    {
        LookAtModel model = NewModel();
        Settle(model, Idle with { InteractionEngaged = true, CursorX = 150.0f, CursorY = 100.0f });
        Assert.Equal(1.0f, model.PupilOffsetX, 4);
        Assert.Equal(1.0f, model.PupilOffsetY, 4);

        // Every sample must land on a step boundary of the profile quantization.
        LookAtModel stepping = NewModel();
        LookAtInputs inputs = Idle with { InteractionEngaged = true, CursorX = 37.0f, CursorY = -11.0f };
        for (int frame = 0; frame < 120; frame++)
        {
            stepping.Update(inputs, 1, Frame);
            Assert.Equal(
                MathF.Round(stepping.PupilOffsetX * 4.0f), stepping.PupilOffsetX * 4.0f, 4);
            Assert.Equal(
                MathF.Round(stepping.PupilOffsetY * 4.0f), stepping.PupilOffsetY * 4.0f, 4);
        }
    }

    [Fact]
    public void TickAndTimeAdvance_AreStepIndependent()
    {
        LookAtInputs inputs = Idle with { InteractionEngaged = true, CursorX = 80.0f, CursorY = 25.0f };
        LookAtModel coarse = NewModel();
        LookAtModel fine = NewModel();

        coarse.Update(inputs, 12, 0.1);
        for (int frame = 0; frame < 12; frame++)
        {
            fine.Update(inputs, 1, 0.1 / 12.0);
        }

        Assert.Equal(coarse.CurrentYawDegrees, fine.CurrentYawDegrees, 4);
        Assert.Equal(coarse.CurrentPitchDegrees, fine.CurrentPitchDegrees, 4);
    }

    [Fact]
    public void NegativeTicks_Throw()
    {
        LookAtModel model = NewModel();
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Update(Idle, -1, Frame));
    }

    [Theory]
    [InlineData("cone")]
    [InlineData("ease")]
    [InlineData("depth")]
    [InlineData("glance")]
    [InlineData("pupil")]
    public void InvalidParameters_Throw(string field)
    {
        LookAtParameters invalid = field switch
        {
            "cone" => Parameters with { ConeYawDegrees = 0.0f },
            "ease" => Parameters with { EaseSeconds = float.NaN },
            "depth" => Parameters with { GazeDepth = -1.0f },
            "glance" => Parameters with { GlanceHoldMaximumTicks = 10 },
            _ => Parameters with { PupilQuantizationSteps = 1 },
        };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LookAtModel(new ScriptedRandomSource(), invalid));
    }
}

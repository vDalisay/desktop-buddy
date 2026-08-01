using DesktopBuddy.Domain.Presentation;
using Xunit;

namespace DesktopBuddy.Domain.Tests.Presentation;

public sealed class ConsumeGestureTests
{
    /// <summary>The shipped Meal schedule, exactly as the activity profile authors it.</summary>
    private static ConsumeGesture Meal =>
        ConsumeGesture.Bites(chestHoldTicks: 60, biteCount: 5, biteCycleTicks: 72,
            biteMoment: 0.55f, finalLowerHoldTicks: 30);

    /// <summary>The Drink: raised over half a second, held for two, then gone.</summary>
    private static ConsumeGesture Drink =>
        ConsumeGesture.SingleRaise(chestHoldTicks: 30, raiseTicks: 60, holdTicks: 240);

    // ---- Shape -------------------------------------------------------------

    [Fact]
    public void TheMealKeepsItsShippedSchedule()
    {
        ConsumeGesture meal = Meal;

        Assert.Equal(ConsumeGestureStyle.Bites, meal.Style);
        Assert.Equal(5, meal.StepCount);
        // 60 chest hold + 5 x 72 bite cycles + 30 final lower, unchanged from M4.
        Assert.Equal(450, meal.TotalTicks);
        Assert.True(meal.IsValid);
    }

    [Fact]
    public void TheDrinkIsOneStep()
    {
        ConsumeGesture drink = Drink;

        Assert.Equal(ConsumeGestureStyle.SingleRaise, drink.Style);
        Assert.Equal(1, drink.StepCount);
        Assert.True(drink.IsValid);
    }

    [Fact]
    public void TheDrinkRaisesForTheAuthoredTimeAndHoldsForTheRest()
    {
        ConsumeGesture drink = Drink;

        // The authored raise, then the authored hold, then a mirrored return.
        Assert.Equal(60 + 240 + 60, drink.StepCycleTicks);

        // Half a second in, it is still on the way up.
        ConsumeGestureSample midRaise = drink.Sample(drink.ChestHoldTicks + 30, 0);
        Assert.InRange(midRaise.Lift, 0.05f, 0.95f);

        // The plateau is exactly the authored two seconds, measured tick by tick.
        int held = 0;
        for (int tick = 0; tick < drink.TotalTicks; tick++)
        {
            if (drink.Sample(tick, 0).Lift >= 0.999f)
                held++;
        }

        // A couple of ticks of slack at each end, where the easing is already flat to three
        // decimal places but the window has not formally opened.
        Assert.InRange(held, 238, 246);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void MalformedGesturesAreRejected(int cycleTicks)
    {
        var broken = new ConsumeGesture(
            ConsumeGestureStyle.Bites, 60, 0, cycleTicks, 0.55f, 30, 0.35f, 0.68f);
        Assert.False(broken.IsValid);
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.0f)]
    [InlineData(float.NaN)]
    public void AStepMomentOutsideItsCycleIsRejected(float moment)
    {
        var broken = new ConsumeGesture(
            ConsumeGestureStyle.Bites, 60, 5, 72, moment, 30, 0.35f, 0.68f);
        Assert.False(broken.IsValid);
    }

    // ---- The chest hold ----------------------------------------------------

    [Fact]
    public void NothingHappensDuringTheChestHold()
    {
        ConsumeGesture meal = Meal;

        for (int tick = 0; tick < meal.ChestHoldTicks; tick++)
        {
            ConsumeGestureSample sample = meal.Sample(tick, 0);
            Assert.Equal(0.0f, sample.Lift);
            Assert.Equal(0.0f, sample.FinalLowering);
            Assert.Equal(0, sample.CompletedSteps);
        }
    }

    // ---- Steps -------------------------------------------------------------

    [Fact]
    public void EveryStepLandsExactlyOncePerCycle()
    {
        ConsumeGesture meal = Meal;
        int completed = 0;
        var stepTicks = new System.Collections.Generic.List<int>();

        for (int tick = 0; tick < meal.TotalTicks; tick++)
        {
            ConsumeGestureSample sample = meal.Sample(tick, completed);
            if (sample.CompletedSteps > completed)
            {
                stepTicks.Add(tick);
                completed = sample.CompletedSteps;
            }
        }

        Assert.Equal(meal.StepCount, completed);
        Assert.Equal(meal.StepCount, stepTicks.Count);
        // Evenly spaced by the cycle length: this is what makes the five bites read as bites.
        for (int index = 1; index < stepTicks.Count; index++)
            Assert.Equal(meal.StepCycleTicks, stepTicks[index] - stepTicks[index - 1]);
    }

    [Fact]
    public void TheMealsFirstBiteLandsAtTheAuthoredMoment()
    {
        ConsumeGesture meal = Meal;
        // 60 chest hold + round(0.55 * 72) = 60 + 40.
        ConsumeGestureSample before = meal.Sample(99, 0);
        ConsumeGestureSample at = meal.Sample(100, 0);

        Assert.Equal(0, before.CompletedSteps);
        Assert.Equal(1, at.CompletedSteps);
    }

    [Fact]
    public void TheDrinksOneStepLandsAfterTheHoldNotBeforeIt()
    {
        ConsumeGesture drink = Drink;
        int completed = 0;
        int stepTick = -1;

        for (int tick = 0; tick < drink.TotalTicks; tick++)
        {
            ConsumeGestureSample sample = drink.Sample(tick, completed);
            if (sample.CompletedSteps > completed)
            {
                stepTick = tick;
                completed = sample.CompletedSteps;
            }
        }

        Assert.Equal(1, completed);
        // Exactly at the end of the raise plus the two-second hold, never during the lift.
        Assert.Equal(drink.ChestHoldTicks + 60 + 240, stepTick);
    }

    [Fact]
    public void ACountThatHasAlreadyFinishedNeverAdvancesAgain()
    {
        ConsumeGesture meal = Meal;

        for (int tick = 0; tick < meal.TotalTicks; tick++)
        {
            ConsumeGestureSample sample = meal.Sample(tick, meal.StepCount);
            Assert.Equal(meal.StepCount, sample.CompletedSteps);
        }
    }

    // ---- The lift curve ----------------------------------------------------

    [Fact]
    public void TheLiftRisesHoldsAndReturnsWithinEachCycle()
    {
        ConsumeGesture meal = Meal;
        int cycleStart = meal.ChestHoldTicks;

        Assert.Equal(0.0f, meal.Sample(cycleStart, 0).Lift, 3);
        Assert.Equal(1.0f, meal.Sample(cycleStart + (int)(72 * 0.5f), 0).Lift, 3);
        Assert.True(meal.Sample(cycleStart + 71, 0).Lift < 0.2f);
    }

    [Fact]
    public void TheLiftNeverLeavesItsRange()
    {
        ConsumeGesture meal = Meal;
        int completed = 0;
        for (int tick = 0; tick < meal.TotalTicks; tick++)
        {
            ConsumeGestureSample sample = meal.Sample(tick, completed);
            completed = sample.CompletedSteps;
            Assert.InRange(sample.Lift, 0.0f, 1.0f);
            Assert.InRange(sample.FinalLowering, 0.0f, 1.0f);
            Assert.InRange(sample.CycleProgress, 0.0f, 1.0f);
        }
    }

    [Fact]
    public void TheFinalLoweringOnlyRunsOnceEveryStepHasLanded()
    {
        ConsumeGesture meal = Meal;

        // Mid-gesture, with steps outstanding, there is no closing lower.
        Assert.Equal(0.0f, meal.Sample(meal.ChestHoldTicks + 70, 1).FinalLowering, 3);

        // On the last cycle's return, with every step landed, it closes.
        int lastCycleReturn = meal.ChestHoldTicks + (4 * 72) + 70;
        Assert.True(meal.Sample(lastCycleReturn, meal.StepCount).FinalLowering > 0.0f);
    }

    [Fact]
    public void PastTheSequenceTheGestureIsFullyLowered()
    {
        ConsumeGesture meal = Meal;
        ConsumeGestureSample tail = meal.Sample(meal.TotalTicks - 1, meal.StepCount);

        Assert.Equal(0.0f, tail.Lift, 3);
        Assert.Equal(1.0f, tail.FinalLowering, 3);
    }
}

using TypedPond.Core;
using Xunit;

namespace TypedPond.Tests;

public class UnlockEvaluatorTests
{
    // A time-of-day comfortably after the default fallback hour (noon), used
    // when the test is not specifically about the fallback window.
    private const int Afternoon = 14;
    private const int FallbackHour = 12;

    [Fact]
    public void TodayStepsExactlyMeetGoal_Unlocks()
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: 10000, yesterdaySteps: null, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Daily goal met", result.Reason);
    }

    [Fact]
    public void TodayStepsExceedGoal_Unlocks()
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: 15000, yesterdaySteps: null, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Daily goal met", result.Reason);
    }

    [Fact]
    public void TodayGoalMet_UnlocksRegardlessOfHour()
    {
        // Meeting the goal always unlocks, even before the fallback window opens.
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: 12000, yesterdaySteps: null, goal: 10000,
            currentHour: 6, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Daily goal met", result.Reason);
    }

    [Fact]
    public void TodayStepsBelowGoal_NoYesterday_StaysLocked()
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: 4000, yesterdaySteps: null, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.False(result.ShouldUnlock);
        Assert.Equal("Steps: 4000/10000", result.Reason);
    }

    [Fact]
    public void TodayNull_YesterdayMeetsGoal_AfterFallbackHour_UnlocksWithFallbackReason()
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: null, yesterdaySteps: 12000, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Using yesterday's data (today unavailable)", result.Reason);
        Assert.Contains("yesterday", result.Reason);
    }

    [Fact]
    public void TodayNull_YesterdayExactlyMeetsGoal_AfterFallbackHour_Unlocks()
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: null, yesterdaySteps: 10000, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Using yesterday's data (today unavailable)", result.Reason);
    }

    [Fact]
    public void TodayNull_YesterdayMeetsGoal_BeforeFallbackHour_StaysLocked()
    {
        // This is the key anti-exploit case: at the start of a new day today's
        // data does not exist yet, but the machine must NOT coast on yesterday's
        // success. Before the fallback hour, stay locked.
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: null, yesterdaySteps: 20000, goal: 10000,
            currentHour: 7, fallbackAfterHour: FallbackHour);

        Assert.False(result.ShouldUnlock);
        Assert.Equal("Steps: 0/10000", result.Reason);
    }

    [Fact]
    public void TodayNull_YesterdayMeetsGoal_ExactlyAtFallbackHour_Unlocks()
    {
        // Boundary: the fallback window opens at (>=) the configured hour.
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: null, yesterdaySteps: 20000, goal: 10000,
            currentHour: FallbackHour, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Using yesterday's data (today unavailable)", result.Reason);
    }

    [Fact]
    public void FallbackDisabled_TodayNull_YesterdayMeetsGoal_StaysLocked()
    {
        // fallbackAfterHour = 24 disables the yesterday fallback entirely.
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: null, yesterdaySteps: 20000, goal: 10000,
            currentHour: 23, fallbackAfterHour: 24);

        Assert.False(result.ShouldUnlock);
        Assert.Equal("Steps: 0/10000", result.Reason);
    }

    [Fact]
    public void TodayNull_YesterdayBelowGoal_AfterFallbackHour_StaysLocked()
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: null, yesterdaySteps: 3000, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.False(result.ShouldUnlock);
        // todaySteps is null, so the "??" yields 0 in the reason string.
        Assert.Equal("Steps: 0/10000", result.Reason);
    }

    [Fact]
    public void TodayNull_YesterdayNull_StaysLocked()
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: null, yesterdaySteps: null, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.False(result.ShouldUnlock);
        Assert.Equal("Steps: 0/10000", result.Reason);
    }

    [Fact]
    public void TodayBelowGoal_YesterdayMeetsGoal_StaysLocked_TodayTakesPrecedence()
    {
        // Today's data is present (below goal). The yesterday fallback only
        // applies when today's data is NULL, so this must stay locked even
        // after the fallback hour.
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: 2000, yesterdaySteps: 20000, goal: 10000,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.False(result.ShouldUnlock);
        Assert.Equal("Steps: 2000/10000", result.Reason);
    }

    [Theory]
    [InlineData(0, 10000, "Steps: 0/10000")]
    [InlineData(9999, 10000, "Steps: 9999/10000")]
    [InlineData(500, 1000, "Steps: 500/1000")]
    public void BelowGoal_ProducesExactReasonString(int todaySteps, int goal, string expectedReason)
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps, yesterdaySteps: null, goal,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.False(result.ShouldUnlock);
        Assert.Equal(expectedReason, result.Reason);
    }

    [Theory]
    [InlineData(10000, 10000)]
    [InlineData(10001, 10000)]
    [InlineData(1, 1)]
    public void AtOrAboveGoal_Unlocks(int todaySteps, int goal)
    {
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps, yesterdaySteps: null, goal,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Daily goal met", result.Reason);
    }

    [Fact]
    public void ZeroGoal_TodayZero_Unlocks()
    {
        // With a goal of 0, any non-null today count (including 0) meets the goal.
        var result = UnlockEvaluator.EvaluateUnlock(
            todaySteps: 0, yesterdaySteps: null, goal: 0,
            currentHour: Afternoon, fallbackAfterHour: FallbackHour);

        Assert.True(result.ShouldUnlock);
        Assert.Equal("Daily goal met", result.Reason);
    }
}

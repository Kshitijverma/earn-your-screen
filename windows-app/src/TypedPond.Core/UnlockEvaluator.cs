namespace TypedPond.Core;

/// <summary>Result of an unlock evaluation.</summary>
public record UnlockResult(bool ShouldUnlock, string Reason);

/// <summary>
/// Stateless evaluator that decides whether the machine should unlock
/// based on step counts and the daily goal.
/// </summary>
public static class UnlockEvaluator
{
    /// <summary>
    /// Evaluates whether to unlock.
    /// - Today's steps meet the goal: unlock.
    /// - Today's data is genuinely unavailable (no report by the fallback hour)
    ///   but yesterday met the goal: unlock (one-day grace for an offline phone).
    /// - Otherwise: stay locked.
    /// </summary>
    /// <param name="todaySteps">Today's step count, or null if none recorded.</param>
    /// <param name="yesterdaySteps">Yesterday's step count, or null.</param>
    /// <param name="goal">Daily step goal.</param>
    /// <param name="currentHour">
    /// Current local hour (0-23). The yesterday fallback is suppressed before
    /// <paramref name="fallbackAfterHour"/> so a fresh day (no data yet) stays
    /// locked rather than coasting on yesterday's success.
    /// </param>
    /// <param name="fallbackAfterHour">
    /// Hour after which the yesterday fallback may apply. Use 24 to disable it.
    /// </param>
    public static UnlockResult EvaluateUnlock(
        int? todaySteps,
        int? yesterdaySteps,
        int goal,
        int currentHour,
        int fallbackAfterHour)
    {
        if (todaySteps.HasValue && todaySteps.Value >= goal)
        {
            return new UnlockResult(true, "Daily goal met");
        }

        // The yesterday fallback exists for a phone that is offline all day, not
        // for the normal empty state at the start of a new day. Only honor it
        // once enough of the day has passed that a live phone would have
        // reported. This also self-limits the grace to a single day: tomorrow,
        // "yesterday" is today's (locked, null) value.
        bool fallbackWindowOpen = currentHour >= fallbackAfterHour;
        if (!todaySteps.HasValue
            && fallbackWindowOpen
            && yesterdaySteps.HasValue
            && yesterdaySteps.Value >= goal)
        {
            return new UnlockResult(true, "Using yesterday's data (today unavailable)");
        }

        return new UnlockResult(false, $"Steps: {todaySteps ?? 0}/{goal}");
    }
}

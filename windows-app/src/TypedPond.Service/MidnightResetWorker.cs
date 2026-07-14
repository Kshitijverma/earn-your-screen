namespace TypedPond.Service;

/// <summary>
/// Re-engages the lock at local midnight each day so the new day always
/// starts locked until that day's step goal is met.
/// </summary>
public class MidnightResetWorker : BackgroundService
{
    private readonly LockManager _lockManager;
    private readonly ILogger<MidnightResetWorker> _logger;

    public MidnightResetWorker(LockManager lockManager, ILogger<MidnightResetWorker> logger)
    {
        _lockManager = lockManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Midnight reset worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan untilMidnight = TimeUntilNextMidnight(DateTime.Now);
            _logger.LogInformation(
                "Next midnight reset in {Hours:0}h {Minutes:0}m.",
                untilMidnight.Hours,
                untilMidnight.Minutes);

            try
            {
                await Task.Delay(untilMidnight, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _logger.LogInformation("Midnight reached; re-engaging lock for the new day.");
            try
            {
                _lockManager.Lock();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to re-engage lock at midnight.");
            }

            // Guard against clock jitter landing us exactly on midnight and
            // computing a zero/negative delay on the next loop iteration.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Midnight reset worker stopping.");
    }

    /// <summary>Time from <paramref name="now"/> until the next local midnight.</summary>
    internal static TimeSpan TimeUntilNextMidnight(DateTime now)
    {
        DateTime nextMidnight = now.Date.AddDays(1);
        TimeSpan delay = nextMidnight - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
    }
}

using TypedPond.Core;

namespace TypedPond.Service;

/// <summary>
/// Polls Firebase periodically as a backup source of step data (in case the
/// Android app cannot reach the laptop directly) and picks up remote changes
/// to the step goal.
/// </summary>
public class FirebasePollWorker : BackgroundService
{
    private readonly Config _config;
    private readonly StepStore _stepStore;
    private readonly FirebaseClient _firebaseClient;
    private readonly LockManager _lockManager;
    private readonly ILogger<FirebasePollWorker> _logger;

    public FirebasePollWorker(
        Config config,
        StepStore stepStore,
        FirebaseClient firebaseClient,
        LockManager lockManager,
        ILogger<FirebasePollWorker> logger)
    {
        _config = config;
        _stepStore = stepStore;
        _firebaseClient = firebaseClient;
        _lockManager = lockManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(1, _config.FirebasePollIntervalSeconds));
        _logger.LogInformation(
            "Firebase poll worker started; polling every {IntervalSeconds} seconds.",
            interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during Firebase poll.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Firebase poll worker stopping.");
    }

    private async Task PollOnceAsync()
    {
        _logger.LogInformation("Polling Firebase for step data and goal...");
        bool updated = false;

        int? todaySteps = await _firebaseClient.GetTodayStepsAsync();
        if (todaySteps.HasValue)
        {
            // The Firebase mailbox is UTC-keyed, but the local store and unlock
            // evaluation are keyed by the laptop's local date. In the common
            // case (phone and laptop in the same zone) these coincide. Near a
            // UTC-midnight boundary that falls mid-local-day they can differ for
            // one sync cycle; it self-heals on the next push.
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            await _stepStore.UpsertStepsAsync(today, todaySteps.Value);
            updated = true;
            _logger.LogInformation(
                "Firebase poll: today's steps = {Steps} (stored for {Date}).",
                todaySteps.Value,
                today);
        }
        else
        {
            _logger.LogInformation("Firebase poll: no step data available for today.");
        }

        int? stepGoal = await _firebaseClient.GetStepGoalAsync();
        if (stepGoal.HasValue && stepGoal.Value > 0 && stepGoal.Value != _config.StepGoal)
        {
            _logger.LogInformation(
                "Firebase poll: step goal changed from {OldGoal} to {NewGoal}.",
                _config.StepGoal,
                stepGoal.Value);
            _config.StepGoal = stepGoal.Value;
            updated = true;
        }
        else if (!stepGoal.HasValue)
        {
            _logger.LogInformation("Firebase poll: no remote step goal configured.");
        }

        if (updated)
        {
            _lockManager.EvaluateAndUpdate();
        }
    }
}

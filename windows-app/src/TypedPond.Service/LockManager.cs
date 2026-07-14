using System.Diagnostics;
using System.IO.Pipes;
using TypedPond.Core;

namespace TypedPond.Service;

/// <summary>
/// Manages the lock screen process lifecycle and the named pipe used to talk to it.
///
/// The service acts as the named pipe server ("TypedPondPipe"); the lock screen
/// executable connects as a client after being launched. Commands are single
/// lines: "LOCK" engages the lock screen, "UNLOCK" tells it to restore the
/// shell (launch explorer.exe) and exit.
/// </summary>
public class LockManager : IDisposable
{
    private const string PipeName = "TypedPondPipe";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly Config _config;
    private readonly StepStore _stepStore;
    private readonly ILogger<LockManager> _logger;
    private readonly object _sync = new();

    private NamedPipeServerStream? _pipe;
    private StreamWriter? _writer;
    private Process? _lockScreenProcess;

    public LockManager(Config config, StepStore stepStore, ILogger<LockManager> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _stepStore = stepStore ?? throw new ArgumentNullException(nameof(stepStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether the machine is currently locked. Always starts locked.</summary>
    public bool IsLocked { get; private set; } = true;

    /// <summary>
    /// Engages the lock: launches the lock screen executable if it is not
    /// already running and sends the "LOCK" command over the named pipe.
    /// </summary>
    public void Lock()
    {
        lock (_sync)
        {
            // Already locked with a live lock screen: nothing to do.
            if (IsLocked &&
                _pipe is { IsConnected: true } &&
                _lockScreenProcess is { HasExited: false })
            {
                return;
            }

            try
            {
                EnsureLockScreenRunningAndConnected();
                SendCommand("LOCK");
                IsLocked = true;
                _logger.LogInformation("Lock engaged.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to engage lock.");
            }
        }
    }

    /// <summary>
    /// Releases the lock: sends the "UNLOCK" command over the named pipe. The
    /// lock screen executable then launches explorer.exe and exits on its own.
    /// </summary>
    public void Unlock()
    {
        lock (_sync)
        {
            if (!IsLocked)
            {
                return;
            }

            try
            {
                if (_pipe is { IsConnected: true })
                {
                    SendCommand("UNLOCK");
                }
                else
                {
                    _logger.LogWarning("Unlock requested but the lock screen pipe is not connected.");
                }

                IsLocked = false;
                _logger.LogInformation("Lock released.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send UNLOCK command.");
            }
            finally
            {
                // The lock screen process exits after UNLOCK; drop our end of the pipe.
                CleanUpPipe();
            }
        }
    }

    /// <summary>
    /// Reads today's (and yesterday's) step counts from the store, evaluates
    /// the unlock rules, and locks or unlocks accordingly.
    /// </summary>
    public void EvaluateAndUpdate()
    {
        DateTime now = DateTime.Now;
        string today = now.ToString("yyyy-MM-dd");
        string yesterday = now.AddDays(-1).ToString("yyyy-MM-dd");

        int? todaySteps = _stepStore.GetStepsAsync(today).GetAwaiter().GetResult();
        int? yesterdaySteps = _stepStore.GetStepsAsync(yesterday).GetAwaiter().GetResult();

        UnlockResult result = UnlockEvaluator.EvaluateUnlock(
            todaySteps,
            yesterdaySteps,
            _config.StepGoal,
            now.Hour,
            _config.FallbackAfterHour);
        _logger.LogInformation(
            "Unlock evaluation: shouldUnlock={ShouldUnlock}, reason={Reason}",
            result.ShouldUnlock,
            result.Reason);

        if (result.ShouldUnlock)
        {
            Unlock();
        }
        else
        {
            Lock();
            SendProgress(todaySteps ?? 0, _config.StepGoal);
        }
    }

    /// <summary>
    /// Pushes current step progress to the lock screen so it can update its
    /// ring and count. No-op if the lock screen pipe is not connected.
    /// </summary>
    public void SendProgress(int steps, int goal)
    {
        lock (_sync)
        {
            if (_writer is null || _pipe is not { IsConnected: true })
            {
                return;
            }

            try
            {
                SendCommand($"PROGRESS {steps} {goal}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send progress to the lock screen.");
            }
        }
    }

    private void EnsureLockScreenRunningAndConnected()
    {
        if (_lockScreenProcess is null || _lockScreenProcess.HasExited)
        {
            CleanUpPipe();

            // Create the pipe server before launching so the client can connect immediately.
            _pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.Out,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            _logger.LogInformation("Launching lock screen: {Path}", _config.LockScreenExePath);
            _lockScreenProcess = Process.Start(new ProcessStartInfo
            {
                FileName = _config.LockScreenExePath,
                UseShellExecute = false,
            }) ?? throw new InvalidOperationException(
                $"Failed to start lock screen process: {_config.LockScreenExePath}");
        }
        else if (_pipe is null)
        {
            _pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.Out,
                maxNumberOfServerInstances: 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
        }

        if (!_pipe.IsConnected)
        {
            using var cts = new CancellationTokenSource(ConnectTimeout);
            try
            {
                _pipe.WaitForConnectionAsync(cts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Lock screen did not connect to pipe '{PipeName}' within {ConnectTimeout.TotalSeconds:0} seconds.");
            }

            _writer = new StreamWriter(_pipe) { AutoFlush = true };
            _logger.LogInformation("Lock screen connected to pipe '{PipeName}'.", PipeName);
        }
    }

    private void SendCommand(string command)
    {
        if (_writer is null || _pipe is not { IsConnected: true })
        {
            throw new InvalidOperationException("Named pipe to the lock screen is not connected.");
        }

        _writer.WriteLine(command);
        _logger.LogDebug("Sent command over pipe: {Command}", command);
    }

    private void CleanUpPipe()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (IOException)
        {
            // Broken pipe on dispose is expected when the client has already exited.
        }
        finally
        {
            _writer = null;
        }

        try
        {
            _pipe?.Dispose();
        }
        catch (IOException)
        {
            // Ignore; we are discarding the pipe anyway.
        }
        finally
        {
            _pipe = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            CleanUpPipe();
            _lockScreenProcess?.Dispose();
            _lockScreenProcess = null;
        }
        GC.SuppressFinalize(this);
    }
}

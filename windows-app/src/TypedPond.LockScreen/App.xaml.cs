using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace TypedPond.LockScreen;

/// <summary>
/// Application entry point. Wires the lock window to commands received over
/// the named pipe from the TypedPond service:
///   "LOCK"   -> show the lock screen
///   "UNLOCK" -> hide the lock screen and launch explorer.exe (the shell)
///
/// Pass <c>--standalone</c> to show the lock screen immediately without a
/// service connection (useful for local testing).
/// </summary>
public partial class App : Application
{
    private readonly PipeClient _pipeClient = new();
    private readonly CancellationTokenSource _cts = new();
    private MainWindow? _mainWindow;
    private bool _standalone;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _standalone = e.Args.Any(a =>
            string.Equals(a, "--standalone", StringComparison.OrdinalIgnoreCase));

        // StartupUri creates and shows MainWindow *after* OnStartup returns, so
        // Application.MainWindow is not available yet. Defer wiring until the
        // window has been created. Normal priority (9) runs before the Render
        // pass (7), so in service mode we hide the window before it paints and
        // avoid a lock-screen flash on startup.
        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Initialize));
    }

    private void Initialize()
    {
        // Application.MainWindow is set by StartupUri; cast to our window type.
        _mainWindow = (MainWindow)base.MainWindow!;

        // When Windows is logging off or shutting down, allow the lock window
        // to close cleanly instead of cancelling the OS session-end request.
        SessionEnding += OnSessionEnding;

        if (_standalone)
        {
            // Show immediately for testing.
            ShowLockScreen();
        }
        else
        {
            // Real mode: stay hidden until the service tells us to LOCK.
            _mainWindow.HideLock();
            StartPipeListener();
        }
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        ShutdownApp();
    }

    /// <summary>Tears the lock down and exits the process cleanly.</summary>
    private void ShutdownApp()
    {
        _mainWindow?.AllowClose();
        _cts.Cancel();
        Shutdown();
    }

    private void StartPipeListener()
    {
        // Fire-and-forget background listener; marshals commands to the UI thread.
        _ = _pipeClient.ListenAsync(OnPipeMessage, _cts.Token);
    }

    private void OnPipeMessage(string message)
    {
        // Invoked on a background thread; hop to the UI thread for any UI work.
        Dispatcher.Invoke(() => HandleCommand(message));
    }

    private void HandleCommand(string message)
    {
        string trimmed = message.Trim();

        // Progress updates carry data: "PROGRESS <steps> <goal>".
        if (trimmed.StartsWith("PROGRESS", StringComparison.OrdinalIgnoreCase))
        {
            HandleProgress(trimmed);
            return;
        }

        switch (trimmed.ToUpperInvariant())
        {
            case "LOCK":
                ShowLockScreen();
                break;

            case "UNLOCK":
                Unlock();
                break;

            case "SHUTDOWN":
                ShutdownApp();
                break;

            default:
                // Unknown command: ignore.
                break;
        }
    }

    /// <summary>
    /// Parses a "PROGRESS &lt;steps&gt; &lt;goal&gt;" command and updates the ring,
    /// count, and motivational message. Malformed commands are ignored.
    /// </summary>
    private void HandleProgress(string command)
    {
        if (_mainWindow is null)
        {
            return;
        }

        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !int.TryParse(parts[1], out int steps)
            || !int.TryParse(parts[2], out int goal))
        {
            return;
        }

        _mainWindow.UpdateProgress(steps, goal);
        _mainWindow.SetMotivationalMessage(MotivationalMessage(steps, goal));
    }

    /// <summary>Picks an encouraging message based on progress toward the goal.</summary>
    private static string MotivationalMessage(int steps, int goal)
    {
        double fraction = goal > 0 ? (double)steps / goal : 0.0;

        return fraction switch
        {
            >= 1.0 => "Goal reached! Unlocking...",
            >= 0.75 => "Almost there — keep going!",
            >= 0.4 => "Great progress! Keep moving.",
            > 0.0 => "You've started — let's get those steps in.",
            _ => "Time to get moving! Reach your step goal to unlock.",
        };
    }

    private void ShowLockScreen()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.ShowLock();
    }

    private void Unlock()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.HideLock();
        LaunchShell();
    }

    private static void LaunchShell()
    {
        try
        {
            // Bring the desktop/shell back for the locked account.
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to launch explorer.exe: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Ensure the window will not veto its own close during teardown.
        _mainWindow?.AllowClose();
        SessionEnding -= OnSessionEnding;
        _cts.Cancel();
        _cts.Dispose();
        base.OnExit(e);
    }
}

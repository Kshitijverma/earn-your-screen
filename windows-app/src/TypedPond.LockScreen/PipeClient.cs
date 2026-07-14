using System.IO;
using System.IO.Pipes;
using System.Text;

namespace TypedPond.LockScreen;

/// <summary>
/// Named-pipe client that connects to the TypedPond service and streams
/// newline-delimited UTF-8 commands (e.g. "LOCK", "UNLOCK").
///
/// The client automatically reconnects if the server goes away, using an
/// exponential backoff capped at 5 seconds. It runs until the supplied
/// <see cref="CancellationToken"/> is cancelled.
/// </summary>
public sealed class PipeClient
{
    /// <summary>Well-known pipe name shared with the service.</summary>
    public const string PipeName = "TypedPondPipe";

    private static readonly TimeSpan InitialBackoff = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly string _pipeName;

    public PipeClient(string pipeName = PipeName)
    {
        _pipeName = pipeName;
    }

    /// <summary>
    /// Connects to the pipe and invokes <paramref name="onMessage"/> for each
    /// complete line received. Reconnects on disconnect until cancelled.
    /// </summary>
    /// <param name="onMessage">Callback for each received command line (trimmed).</param>
    /// <param name="ct">Token used to stop listening.</param>
    public async Task ListenAsync(Action<string> onMessage, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onMessage);

        TimeSpan backoff = InitialBackoff;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.In,
                    PipeOptions.Asynchronous);

                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(ConnectTimeout);
                await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

                // Connected successfully: reset backoff for the next outage.
                backoff = InitialBackoff;

                await ReadLoopAsync(pipe, onMessage, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Normal shutdown.
                return;
            }
            catch (OperationCanceledException)
            {
                // Connect timed out (not overall cancellation): fall through
                // to backoff and retry.
            }
            catch (IOException)
            {
                // Pipe broke; retry after backoff.
            }
            catch (TimeoutException)
            {
                // Retry after backoff.
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(backoff, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            backoff = NextBackoff(backoff);
        }
    }

    private static async Task ReadLoopAsync(NamedPipeClientStream pipe, Action<string> onMessage, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

        while (!ct.IsCancellationRequested && pipe.IsConnected)
        {
            string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                // End of stream: the server closed the pipe.
                return;
            }

            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                onMessage(trimmed);
            }
        }
    }

    private static TimeSpan NextBackoff(TimeSpan current)
    {
        double doubledMs = current.TotalMilliseconds * 2;
        return doubledMs >= MaxBackoff.TotalMilliseconds
            ? MaxBackoff
            : TimeSpan.FromMilliseconds(doubledMs);
    }
}

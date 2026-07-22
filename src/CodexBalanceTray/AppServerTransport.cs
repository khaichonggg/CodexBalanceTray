using System.Diagnostics;

namespace CodexBalanceTray;

internal sealed record CodexLaunchSpec(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string? CodexHomeOverride = null);

internal interface IAppServerTransport : IAsyncDisposable
{
    event Action? Disconnected;

    bool IsRunning { get; }

    Task StartAsync(Func<string, Task> onLine, CancellationToken cancellationToken);

    ValueTask WriteLineAsync(string line, CancellationToken cancellationToken);
}

internal sealed class ProcessAppServerTransport : IAppServerTransport
{
    private readonly CodexLaunchSpec _launchSpec;
    private readonly IAppLogger _logger;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _lifetime;
    private int _disconnectSignalled;
    private bool _intentionalShutdown;

    public ProcessAppServerTransport(CodexLaunchSpec launchSpec, IAppLogger logger)
    {
        _launchSpec = launchSpec;
        _logger = logger;
    }

    public event Action? Disconnected;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public Task StartAsync(Func<string, Task> onLine, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_intentionalShutdown, this);

        var startInfo = new ProcessStartInfo
        {
            FileName = _launchSpec.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        foreach (var argument in _launchSpec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(_launchSpec.CodexHomeOverride))
        {
            startInfo.Environment["CODEX_HOME"] = _launchSpec.CodexHomeOverride;
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.Exited += (_, _) => SignalDisconnected();
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("Unable to start the Codex app-server.");
        }

        lock (_gate)
        {
            _process = process;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        _ = ReadOutputAsync(process, onLine, _lifetime.Token);
        _ = DrainErrorAsync(process, _lifetime.Token);
        _logger.Info("app_server_started");
        return Task.CompletedTask;
    }

    public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        Process process;
        lock (_gate)
        {
            process = _process
                ?? throw new InvalidOperationException("The Codex app-server is not running.");
        }

        await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _intentionalShutdown = true;

        Process? process;
        CancellationTokenSource? lifetime;
        lock (_gate)
        {
            process = _process;
            lifetime = _lifetime;
            _process = null;
            _lifetime = null;
        }

        lifetime?.Cancel();

        if (process is not null)
        {
            try
            {
                process.StandardInput.Close();
                if (!process.HasExited)
                {
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(750));
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException
                or TimeoutException
                or OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                    // The process exited between the checks.
                }
            }
            finally
            {
                process.Dispose();
            }
        }

        lifetime?.Dispose();
    }

    private async Task ReadOutputAsync(
        Process process,
        Func<string, Task> onLine,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                await onLine(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (IOException exception)
        {
            _logger.Error("app_server_stdout_failed", exception);
        }
        finally
        {
            SignalDisconnected();
        }
    }

    private async Task DrainErrorAsync(Process process, CancellationToken cancellationToken)
    {
        var sawOutput = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                sawOutput = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (IOException exception)
        {
            _logger.Error("app_server_stderr_failed", exception);
        }

        if (sawOutput)
        {
            _logger.Warn("app_server_stderr_output_redacted");
        }
    }

    private void SignalDisconnected()
    {
        if (_intentionalShutdown
            || Interlocked.Exchange(ref _disconnectSignalled, 1) != 0)
        {
            return;
        }

        _logger.Warn("app_server_disconnected");
        Disconnected?.Invoke();
    }
}

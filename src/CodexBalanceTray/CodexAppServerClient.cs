using System.Collections.Concurrent;
using System.Text.Json;

namespace CodexBalanceTray;

internal sealed class CodexAppServerClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAppServerTransport _transport;
    private readonly IAppLogger _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _loginWaiters = new();
    private long _nextRequestId;
    private bool _disposed;

    public CodexAppServerClient(IAppServerTransport transport, IAppLogger logger)
    {
        _transport = transport;
        _logger = logger;
    }

    public event Action<TraySnapshot>? RateLimitsUpdated;
    public event Action? AccountUpdated;
    public event Action? Disconnected;

    public bool IsRunning => _transport.IsRunning;

    public static CodexAppServerClient Create(
        string codexExecutable,
        string? codexHomeOverride,
        IAppLogger logger)
    {
        var launchSpec = new CodexLaunchSpec(
            codexExecutable,
            ["app-server", "--listen", "stdio://"],
            codexHomeOverride);
        return new CodexAppServerClient(
            new ProcessAppServerTransport(launchSpec, logger),
            logger);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        _transport.Disconnected += OnTransportDisconnected;
        await _transport.StartAsync(HandleLineAsync, _lifetime.Token);

        await RequestAsync(
            "initialize",
            new
            {
                clientInfo = new
                {
                    name = "codex_balance_tray",
                    title = "Codex Balance Tray",
                    version = "1.0.0"
                },
                capabilities = new
                {
                    optOutNotificationMethods = new[]
                    {
                        "thread/started",
                        "item/agentMessage/delta"
                    }
                }
            },
            cancellationToken);

        await SendNotificationAsync("initialized", cancellationToken);
        _logger.Info("app_server_initialized");
    }

    public async Task<bool> HasAccountAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(
            "account/read",
            new { refreshToken = false },
            cancellationToken);
        return result.TryGetProperty("account", out var account)
            && account.ValueKind == JsonValueKind.Object;
    }

    public async Task<TraySnapshot?> ReadRateLimitsAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(
            "account/rateLimits/read",
            parameters: null,
            cancellationToken);
        return SnapshotParser.ParseRateLimitsResult(result);
    }

    public async Task<LoginStartResult> StartBrowserLoginAsync(CancellationToken cancellationToken)
    {
        var result = await RequestAsync(
            "account/login/start",
            new
            {
                type = "chatgpt",
                useHostedLoginSuccessPage = true,
                appBrand = "codex"
            },
            cancellationToken);

        var loginId = result.GetProperty("loginId").GetString();
        var authUrl = result.GetProperty("authUrl").GetString();
        if (string.IsNullOrWhiteSpace(loginId) || string.IsNullOrWhiteSpace(authUrl))
        {
            throw new InvalidOperationException("Codex did not return a valid login session.");
        }

        _loginWaiters.TryAdd(
            loginId,
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
        return new LoginStartResult(loginId, authUrl);
    }

    public async Task<bool> WaitForLoginAsync(
        string loginId,
        CancellationToken cancellationToken)
    {
        if (!_loginWaiters.TryGetValue(loginId, out var waiter))
        {
            throw new InvalidOperationException("Unknown Codex login session.");
        }

        try
        {
            return await waiter.Task.WaitAsync(TimeSpan.FromMinutes(10), cancellationToken);
        }
        finally
        {
            _loginWaiters.TryRemove(loginId, out _);
        }
    }

    public async Task CancelLoginAsync(string loginId, CancellationToken cancellationToken)
    {
        await RequestAsync(
            "account/login/cancel",
            new { loginId },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _transport.Disconnected -= OnTransportDisconnected;

        var exception = new OperationCanceledException("Codex app-server client closed.");
        foreach (var waiter in _pending.Values)
        {
            waiter.TrySetException(exception);
        }

        foreach (var waiter in _loginWaiters.Values)
        {
            waiter.TrySetException(exception);
        }

        _pending.Clear();
        _loginWaiters.Clear();
        await _transport.DisposeAsync();
        _lifetime.Dispose();
    }

    private async Task<JsonElement> RequestAsync(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(requestId, completion))
        {
            throw new InvalidOperationException("Unable to allocate a Codex request identifier.");
        }

        try
        {
            var message = parameters is null
                ? JsonSerializer.Serialize(new { method, id = requestId }, JsonOptions)
                : JsonSerializer.Serialize(new { method, id = requestId, @params = parameters }, JsonOptions);
            await _transport.WriteLineAsync(message, cancellationToken);
            return await completion.Task.WaitAsync(RequestTimeout, cancellationToken);
        }
        finally
        {
            _pending.TryRemove(requestId, out _);
        }
    }

    private ValueTask SendNotificationAsync(string method, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Serialize(new { method }, JsonOptions);
        return _transport.WriteLineAsync(message, cancellationToken);
    }

    private Task HandleLineAsync(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.TryGetProperty("id", out var idElement)
                && idElement.TryGetInt64(out var requestId)
                && _pending.TryGetValue(requestId, out var completion))
            {
                if (root.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }
                else
                {
                    completion.TrySetException(
                        new InvalidOperationException("Codex app-server request failed."));
                }

                return Task.CompletedTask;
            }

            if (!root.TryGetProperty("method", out var methodElement))
            {
                return Task.CompletedTask;
            }

            var method = methodElement.GetString();
            if (!root.TryGetProperty("params", out var parameters))
            {
                parameters = default;
            }

            switch (method)
            {
                case "account/rateLimits/updated":
                    {
                        var snapshot = SnapshotParser.ParseRateLimitsResult(parameters);
                        if (snapshot is not null)
                        {
                            RateLimitsUpdated?.Invoke(snapshot);
                        }

                        break;
                    }
                case "account/updated":
                    AccountUpdated?.Invoke();
                    break;
                case "account/login/completed":
                    CompleteLogin(parameters);
                    break;
            }
        }
        catch (JsonException exception)
        {
            _logger.Error("app_server_invalid_json", exception);
        }

        return Task.CompletedTask;
    }

    private void CompleteLogin(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("loginId", out var loginIdElement)
            || loginIdElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var loginId = loginIdElement.GetString();
        var success = parameters.TryGetProperty("success", out var successElement)
            && successElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            && successElement.GetBoolean();

        if (!string.IsNullOrWhiteSpace(loginId)
            && _loginWaiters.TryGetValue(loginId, out var waiter))
        {
            waiter.TrySetResult(success);
        }
    }

    private void OnTransportDisconnected()
    {
        if (_disposed)
        {
            return;
        }

        var exception = new IOException("Codex app-server disconnected.");
        foreach (var completion in _pending.Values)
        {
            completion.TrySetException(exception);
        }

        Disconnected?.Invoke();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

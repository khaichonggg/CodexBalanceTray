using System.Text.Json;

namespace CodexBalanceTray.Tests;

public sealed class CodexAppServerClientTests
{
    [Fact]
    public async Task Client_InitializesReadsAccountAndParsesRateLimits()
    {
        await using var transport = new FakeAppServerTransport();
        await using var client = new CodexAppServerClient(transport, NullAppLogger.Instance);

        await client.StartAsync(CancellationToken.None);
        var hasAccount = await client.HasAccountAsync(CancellationToken.None);
        var snapshot = await client.ReadRateLimitsAsync(CancellationToken.None);

        Assert.True(hasAccount);
        Assert.NotNull(snapshot);
        Assert.Equal(72, Assert.Single(snapshot.Windows).RemainingPercent);
        Assert.Equal("10", snapshot.Credits?.Balance);
        Assert.Contains("initialize", transport.ReceivedMethods);
        Assert.Contains("initialized", transport.ReceivedMethods);
    }

    [Fact]
    public async Task Client_RaisesPushedRateLimitUpdates()
    {
        await using var transport = new FakeAppServerTransport();
        await using var client = new CodexAppServerClient(transport, NullAppLogger.Instance);
        var completion = new TaskCompletionSource<TraySnapshot>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.RateLimitsUpdated += snapshot => completion.TrySetResult(snapshot);

        await client.StartAsync(CancellationToken.None);
        await transport.PushAsync("""
        {
          "method": "account/rateLimits/updated",
          "params": {
            "rateLimits": {
              "primary": { "usedPercent": 70, "windowDurationMins": 300 },
              "spendControlReached": false
            }
          }
        }
        """);

        var snapshot = await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(30, Assert.Single(snapshot.Windows).RemainingPercent);
    }

    [Fact]
    public async Task Client_CompletesBrowserLoginNotification()
    {
        await using var transport = new FakeAppServerTransport();
        await using var client = new CodexAppServerClient(transport, NullAppLogger.Instance);

        await client.StartAsync(CancellationToken.None);
        var login = await client.StartBrowserLoginAsync(CancellationToken.None);
        var waitTask = client.WaitForLoginAsync(login.LoginId, CancellationToken.None);
        await transport.PushAsync("""
        {
          "method": "account/login/completed",
          "params": {
            "loginId": "login-test",
            "success": true,
            "error": null
          }
        }
        """);

        Assert.True(await waitTask.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.StartsWith("https://", login.AuthUrl);
    }

    [Fact]
    public async Task Client_ReportsTransportDisconnect()
    {
        await using var transport = new FakeAppServerTransport();
        await using var client = new CodexAppServerClient(transport, NullAppLogger.Instance);
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        client.Disconnected += completion.SetResult;

        await client.StartAsync(CancellationToken.None);
        transport.Disconnect();

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(client.IsRunning);
    }

    private sealed class FakeAppServerTransport : IAppServerTransport
    {
        private Func<string, Task>? _onLine;

        public event Action? Disconnected;

        public bool IsRunning { get; private set; }

        public List<string> ReceivedMethods { get; } = [];

        public Task StartAsync(Func<string, Task> onLine, CancellationToken cancellationToken)
        {
            _onLine = onLine;
            IsRunning = true;
            return Task.CompletedTask;
        }

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString() ?? string.Empty;
            ReceivedMethods.Add(method);

            if (!root.TryGetProperty("id", out var idElement))
            {
                return;
            }

            var id = idElement.GetInt64();
            var response = method switch
            {
                "initialize" => JsonSerializer.Serialize(new
                {
                    id,
                    result = new
                    {
                        userAgent = "fake",
                        codexHome = @"E:\fake",
                        platformFamily = "windows",
                        platformOs = "windows"
                    }
                }),
                "account/read" => JsonSerializer.Serialize(new
                {
                    id,
                    result = new
                    {
                        account = new { type = "chatgpt", planType = "plus" },
                        requiresOpenaiAuth = true
                    }
                }),
                "account/rateLimits/read" => JsonSerializer.Serialize(new
                {
                    id,
                    result = new
                    {
                        rateLimits = new
                        {
                            primary = new
                            {
                                usedPercent = 28,
                                windowDurationMins = 300,
                                resetsAt = 1_785_258_687L
                            },
                            secondary = (object?)null,
                            credits = new
                            {
                                hasCredits = true,
                                unlimited = false,
                                balance = "10"
                            },
                            spendControlReached = false,
                            planType = "plus",
                            rateLimitReachedType = (string?)null
                        }
                    }
                }),
                "account/login/start" => JsonSerializer.Serialize(new
                {
                    id,
                    result = new
                    {
                        type = "chatgpt",
                        loginId = "login-test",
                        authUrl = "https://example.test/login"
                    }
                }),
                _ => JsonSerializer.Serialize(new { id, result = new { } })
            };

            if (_onLine is not null)
            {
                await _onLine(response);
            }
        }

        public Task PushAsync(string line) => _onLine?.Invoke(line) ?? Task.CompletedTask;

        public void Disconnect()
        {
            IsRunning = false;
            Disconnected?.Invoke();
        }

        public ValueTask DisposeAsync()
        {
            IsRunning = false;
            return ValueTask.CompletedTask;
        }
    }
}

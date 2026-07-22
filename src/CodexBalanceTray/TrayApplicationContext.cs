using System.Diagnostics;
using Microsoft.Win32;

namespace CodexBalanceTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5)
    ];

    private readonly AppPaths _paths;
    private readonly IAppLogger _logger;
    private readonly SettingsStore _store;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _currentAccountItem;
    private readonly ToolStripMenuItem _independentAccountItem;
    private readonly ToolStripMenuItem _startupItem;

    private AppSettings _settings;
    private TraySnapshot? _lastSnapshot;
    private CodexAppServerClient? _client;
    private SynchronizationContext? _uiContext;
    private CancellationTokenSource? _reconnectCancellation;
    private Task? _reconnectTask;
    private Icon? _currentIcon;
    private bool _exiting;

    public TrayApplicationContext()
    {
        _paths = new AppPaths();
        _paths.EnsureDirectories();
        _logger = new AppLogger(_paths.Logs);
        _store = new SettingsStore(_paths, _logger);
        _settings = _store.LoadSettings();
        _lastSnapshot = _store.LoadSnapshot()?.AsStale();

        _currentAccountItem = new ToolStripMenuItem("使用当前 Codex 账号")
        {
            CheckOnClick = false
        };
        _currentAccountItem.Click += async (_, _) =>
            await SelectAccountSourceAsync(AccountSource.CurrentCodex);

        _independentAccountItem = new ToolStripMenuItem("使用已连接的独立账号")
        {
            CheckOnClick = false
        };
        _independentAccountItem.Click += async (_, _) =>
            await SelectAccountSourceAsync(AccountSource.Independent);

        var connectIndependentItem = new ToolStripMenuItem("连接或切换独立 ChatGPT 账号…");
        connectIndependentItem.Click += async (_, _) => await ConnectIndependentAccountAsync();

        var refreshItem = new ToolStripMenuItem("立即刷新");
        refreshItem.Click += async (_, _) => await RefreshAsync();

        _startupItem = new ToolStripMenuItem("开机启动")
        {
            Checked = _settings.StartWithWindows,
            CheckOnClick = false
        };
        _startupItem.Click += (_, _) => ToggleStartup();

        var aboutItem = new ToolStripMenuItem("关于");
        aboutItem.Click += (_, _) => ShowAbout();

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (_, _) => ExitThread();

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.AddRange(
        [
            _currentAccountItem,
            _independentAccountItem,
            connectIndependentItem,
            new ToolStripSeparator(),
            refreshItem,
            _startupItem,
            new ToolStripSeparator(),
            aboutItem,
            exitItem
        ]);

        _trayIcon = new NotifyIcon
        {
            ContextMenuStrip = contextMenu,
            Text = TooltipFormatter.Format(_lastSnapshot),
            Visible = true
        };
        SetIcon(
            TrayStatusEvaluator.Evaluate(_lastSnapshot),
            TrayStatusEvaluator.GetRemainingPercent(_lastSnapshot));
        UpdateAccountMenu();

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = (int)TimeSpan.FromMinutes(5).TotalMilliseconds
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync();

        Application.Idle += OnFirstIdle;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        try
        {
            var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
            StartupManager.SetEnabled(_settings.StartWithWindows, executablePath);
            _store.SaveSettings(_settings);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or InvalidOperationException)
        {
            _logger.Error("startup_registration_failed", exception);
        }

        _logger.Info("application_started");
    }

    protected override void ExitThreadCore()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        Application.Idle -= OnFirstIdle;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _refreshTimer.Stop();
        _refreshTimer.Dispose();
        _reconnectCancellation?.Cancel();
        _lifetime.Cancel();

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _currentIcon?.Dispose();
        _currentIcon = null;

        if (_client is not null)
        {
            _client.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _client = null;
        }

        _logger.Info("application_stopped");
        _reconnectCancellation?.Dispose();
        _lifetime.Dispose();
        base.ExitThreadCore();
    }

    private async void OnFirstIdle(object? sender, EventArgs eventArgs)
    {
        Application.Idle -= OnFirstIdle;
        _uiContext = SynchronizationContext.Current;
        _refreshTimer.Start();

        var connected = await ConnectSelectedSourceAsync();
        if (!connected)
        {
            StartReconnectLoop();
        }
    }

    private async Task<bool> ConnectSelectedSourceAsync()
    {
        if (_exiting)
        {
            return false;
        }

        await _connectionGate.WaitAsync(_lifetime.Token);
        try
        {
            SetStatusText("Codex｜正在连接…");
            await DisposeClientAsync();

            var codexExecutable = CodexLocator.FindExecutable();
            if (codexExecutable is null)
            {
                SetStatusText("Codex｜找不到官方 Codex，请先安装或更新");
                _logger.Warn("codex_executable_not_found");
                return false;
            }

            var codexHomeOverride = _settings.AccountSource == AccountSource.Independent
                ? _paths.IndependentCodexHome
                : null;
            var client = CodexAppServerClient.Create(
                codexExecutable,
                codexHomeOverride,
                _logger);

            client.RateLimitsUpdated += ignoredSnapshot =>
                PostToUi(() =>
                {
                    _ = ignoredSnapshot;
                    _ = RefreshAsync();
                });
            client.AccountUpdated += () =>
                PostToUi(() => _ = RefreshAsync());
            client.Disconnected += () =>
                PostToUi(() => HandleClientDisconnected(client));

            _client = client;
            await client.StartAsync(_lifetime.Token);

            if (!await client.HasAccountAsync(_lifetime.Token))
            {
                SetStatusText("Codex｜未连接账号，右键选择连接方式");
                _logger.Warn("account_not_connected");
                return true;
            }

            await RefreshClientAsync(client);
            return true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or TimeoutException
            or UnauthorizedAccessException)
        {
            _logger.Error("connection_failed", exception);
            ShowStaleSnapshot();
            await DisposeClientAsync();
            return false;
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private async Task SelectAccountSourceAsync(AccountSource source)
    {
        StopReconnectLoop();
        _settings.AccountSource = source;
        _store.SaveSettings(_settings);
        UpdateAccountMenu();

        if (!await ConnectSelectedSourceAsync())
        {
            StartReconnectLoop();
        }
    }

    private async Task ConnectIndependentAccountAsync()
    {
        StopReconnectLoop();
        _settings.AccountSource = AccountSource.Independent;
        _store.SaveSettings(_settings);
        UpdateAccountMenu();

        if (!await ConnectSelectedSourceAsync() || _client is null)
        {
            StartReconnectLoop();
            return;
        }

        var client = _client;
        try
        {
            SetStatusText("Codex｜等待 ChatGPT 登录…");
            var login = await client.StartBrowserLoginAsync(_lifetime.Token);
            OpenBrowser(login.AuthUrl);

            using var loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetime.Token);
            using var form = new LoginProgressForm(() => OpenBrowser(login.AuthUrl));
            form.CancelRequested += () =>
            {
                loginCancellation.Cancel();
                _ = CancelLoginQuietlyAsync(client, login.LoginId);
            };
            form.Show();

            var success = await client.WaitForLoginAsync(
                login.LoginId,
                loginCancellation.Token);
            form.CompleteAndClose();

            if (success)
            {
                _logger.Info("independent_login_completed");
                await RefreshClientAsync(client);
            }
            else
            {
                SetStatusText("Codex｜ChatGPT 登录未完成");
                MessageBox.Show(
                    "ChatGPT 登录没有完成，请稍后重试。",
                    "CodexBalanceTray",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatusText("Codex｜ChatGPT 登录已取消");
            _logger.Info("independent_login_cancelled");
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or TimeoutException)
        {
            _logger.Error("independent_login_failed", exception);
            SetStatusText("Codex｜ChatGPT 登录失败，请重试");
            MessageBox.Show(
                "无法完成 ChatGPT 登录。请确认网络和官方 Codex 可以正常使用。",
                "CodexBalanceTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private async Task RefreshAsync()
    {
        if (_exiting || !await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var client = _client;
            if (client is null || !client.IsRunning)
            {
                ShowStaleSnapshot();
                return;
            }

            await RefreshClientAsync(client);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or TimeoutException)
        {
            _logger.Error("refresh_failed", exception);
            ShowStaleSnapshot();
            if (_client is not { IsRunning: true })
            {
                StartReconnectLoop();
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshClientAsync(CodexAppServerClient client)
    {
        var snapshot = await client.ReadRateLimitsAsync(_lifetime.Token);
        if (snapshot is null)
        {
            SetStatusText("Codex｜账号未提供额度数据");
            return;
        }

        ApplyFreshSnapshot(snapshot);
        _logger.Info("rate_limits_refreshed");
    }

    private void ApplyFreshSnapshot(TraySnapshot snapshot)
    {
        if (_exiting)
        {
            return;
        }

        _lastSnapshot = snapshot with { IsStale = false };
        _store.SaveSnapshot(_lastSnapshot);
        _trayIcon.Text = TooltipFormatter.Format(_lastSnapshot);
        SetIcon(
            TrayStatusEvaluator.Evaluate(_lastSnapshot),
            TrayStatusEvaluator.GetRemainingPercent(_lastSnapshot));
    }

    private void ShowStaleSnapshot()
    {
        if (_lastSnapshot is not null)
        {
            _lastSnapshot = _lastSnapshot.AsStale();
            _trayIcon.Text = TooltipFormatter.Format(_lastSnapshot);
        }
        else
        {
            _trayIcon.Text = "Codex｜暂时无法读取额度";
        }

        SetIcon(TrayStatusColor.Gray, null);
    }

    private void SetStatusText(string text)
    {
        _trayIcon.Text = text.Length <= TooltipFormatter.MaximumLength
            ? text
            : text[..TooltipFormatter.MaximumLength];
        SetIcon(TrayStatusColor.Gray, null);
    }

    private void SetIcon(TrayStatusColor status, double? remainingPercent)
    {
        var replacement = TrayIconFactory.Create(status, remainingPercent);
        var previous = _currentIcon;
        _trayIcon.Icon = replacement;
        _currentIcon = replacement;
        previous?.Dispose();
    }

    private void HandleClientDisconnected(CodexAppServerClient client)
    {
        if (_exiting || !ReferenceEquals(_client, client))
        {
            return;
        }

        ShowStaleSnapshot();
        StartReconnectLoop();
    }

    private void StartReconnectLoop()
    {
        if (_exiting || _reconnectTask is { IsCompleted: false })
        {
            return;
        }

        _reconnectCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _reconnectTask = ReconnectLoopAsync(_reconnectCancellation.Token);
    }

    private async Task ReconnectLoopAsync(CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = ReconnectDelays[Math.Min(attempt, ReconnectDelays.Length - 1)];
            try
            {
                await Task.Delay(delay, cancellationToken);
                if (await ConnectSelectedSourceAsync())
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            attempt++;
        }
    }

    private void StopReconnectLoop()
    {
        _reconnectCancellation?.Cancel();
        _reconnectCancellation?.Dispose();
        _reconnectCancellation = null;
        _reconnectTask = null;
    }

    private async Task DisposeClientAsync()
    {
        var client = _client;
        _client = null;
        if (client is not null)
        {
            await client.DisposeAsync();
        }
    }

    private async Task CancelLoginQuietlyAsync(
        CodexAppServerClient client,
        string loginId)
    {
        try
        {
            await client.CancelLoginAsync(loginId, _lifetime.Token);
        }
        catch (Exception exception) when (exception is IOException
            or InvalidOperationException
            or OperationCanceledException
            or TimeoutException)
        {
            _logger.Error("login_cancel_failed", exception);
        }
    }

    private void ToggleStartup()
    {
        try
        {
            _settings.StartWithWindows = !_settings.StartWithWindows;
            var executablePath = Environment.ProcessPath ?? Application.ExecutablePath;
            StartupManager.SetEnabled(_settings.StartWithWindows, executablePath);
            _startupItem.Checked = _settings.StartWithWindows;
            _store.SaveSettings(_settings);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or IOException
            or InvalidOperationException)
        {
            _logger.Error("startup_toggle_failed", exception);
            MessageBox.Show(
                "无法修改开机启动设置。",
                "CodexBalanceTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdateAccountMenu()
    {
        _currentAccountItem.Checked = _settings.AccountSource == AccountSource.CurrentCodex;
        _independentAccountItem.Checked = _settings.AccountSource == AccountSource.Independent;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Resume)
        {
            PostToUi(() => _ = RefreshAsync());
        }
    }

    private void PostToUi(Action action)
    {
        if (_exiting)
        {
            return;
        }

        if (_uiContext is null)
        {
            action();
            return;
        }

        _uiContext.Post(_ => action(), null);
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo(url)
        {
            UseShellExecute = true
        });
    }

    private static void ShowAbout()
    {
        MessageBox.Show(
            "CodexBalanceTray 1.0\r\n\r\n悬停查看 Codex 额度。\r\n绿色 >60%，橙色 31–60%，红色 ≤30%。",
            "关于 CodexBalanceTray",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}

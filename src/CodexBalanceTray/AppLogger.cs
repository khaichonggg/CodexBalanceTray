namespace CodexBalanceTray;

internal interface IAppLogger
{
    void Info(string eventName);
    void Warn(string eventName);
    void Error(string eventName, Exception? exception = null);
}

internal sealed class AppLogger : IAppLogger
{
    private readonly string _logsDirectory;
    private readonly object _gate = new();

    public AppLogger(string logsDirectory)
    {
        _logsDirectory = logsDirectory;
        Directory.CreateDirectory(_logsDirectory);
        DeleteExpiredLogs();
    }

    public void Info(string eventName) => Write("INFO", eventName, null);

    public void Warn(string eventName) => Write("WARN", eventName, null);

    public void Error(string eventName, Exception? exception = null) =>
        Write("ERROR", eventName, exception?.GetType().Name);

    private void Write(string level, string eventName, string? exceptionType)
    {
        var safeEventName = new string(
            eventName.Where(character => char.IsLetterOrDigit(character)
                || character is '_' or '-' or '.').ToArray());
        var line = $"{DateTimeOffset.Now:O} {level} {safeEventName}";
        if (!string.IsNullOrWhiteSpace(exceptionType))
        {
            line += $" exception={exceptionType}";
        }

        lock (_gate)
        {
            var path = Path.Combine(
                _logsDirectory,
                $"app-{DateTime.Today:yyyyMMdd}.log");
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private void DeleteExpiredLogs()
    {
        foreach (var path in Directory.EnumerateFiles(_logsDirectory, "app-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A locked old log can safely wait for the next launch.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never prevent the tray app from starting.
            }
        }
    }
}

internal sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    public void Info(string eventName) { }
    public void Warn(string eventName) { }
    public void Error(string eventName, Exception? exception = null) { }
}

namespace CodexBalanceTray;

internal enum AccountSource
{
    CurrentCodex,
    Independent
}

internal enum TrayStatusColor
{
    Green,
    Orange,
    Red,
    Gray
}

internal sealed record RateLimitWindow(
    double UsedPercent,
    int WindowDurationMins,
    long? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);
}

internal sealed record CreditsSnapshot(
    bool HasCredits,
    bool Unlimited,
    string? Balance);

internal sealed record TraySnapshot(
    IReadOnlyList<RateLimitWindow> Windows,
    CreditsSnapshot? Credits,
    bool SpendControlReached,
    string? RateLimitReachedType,
    string? PlanType,
    DateTimeOffset CapturedAt,
    bool IsStale = false)
{
    public TraySnapshot AsStale() => this with { IsStale = true };
}

internal sealed class AppSettings
{
    public AccountSource AccountSource { get; set; } = AccountSource.CurrentCodex;

    public bool StartWithWindows { get; set; } = true;
}

internal sealed record LoginStartResult(string LoginId, string AuthUrl);

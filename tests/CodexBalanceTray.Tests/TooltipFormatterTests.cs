namespace CodexBalanceTray.Tests;

public sealed class TooltipFormatterTests
{
    [Fact]
    public void Format_ShowsQuotaWindowsAndCredits()
    {
        var now = new DateTimeOffset(2026, 7, 22, 20, 0, 0, TimeSpan.FromHours(8));
        var snapshot = new TraySnapshot(
            [
                new RateLimitWindow(28, 300, now.AddHours(2).ToUnixTimeSeconds()),
                new RateLimitWindow(47, 10_080, now.AddDays(6).ToUnixTimeSeconds())
            ],
            new CreditsSnapshot(true, false, "120"),
            SpendControlReached: false,
            RateLimitReachedType: null,
            PlanType: "plus",
            CapturedAt: now);

        var tooltip = TooltipFormatter.Format(snapshot, now);

        Assert.Contains("5h剩余72%", tooltip);
        Assert.Contains("周剩余53%", tooltip);
        Assert.Contains("Credits 120", tooltip);
        Assert.True(tooltip.Length <= TooltipFormatter.MaximumLength);
    }

    [Fact]
    public void Format_ShowsUnlimitedCredits()
    {
        var snapshot = new TraySnapshot(
            [new RateLimitWindow(0, 300, null)],
            new CreditsSnapshot(true, true, null),
            false,
            null,
            "plus",
            DateTimeOffset.Now);

        Assert.Contains("Credits无限", TooltipFormatter.Format(snapshot));
    }

    [Fact]
    public void Format_NeverExceedsNotifyIconLimit()
    {
        var snapshot = new TraySnapshot(
            [new RateLimitWindow(25, 10_080, null)],
            new CreditsSnapshot(true, false, new string('9', 300)),
            false,
            null,
            "plus",
            DateTimeOffset.Now);

        Assert.Equal(
            TooltipFormatter.MaximumLength,
            TooltipFormatter.Format(snapshot).Length);
    }

    [Fact]
    public void Format_MarksCachedDataAsOfflineWithoutReplacingValuesWithZero()
    {
        var snapshot = new TraySnapshot(
            [new RateLimitWindow(47, 10_080, null)],
            new CreditsSnapshot(false, false, "0"),
            false,
            null,
            "plus",
            DateTimeOffset.Now,
            IsStale: true);

        var tooltip = TooltipFormatter.Format(snapshot);

        Assert.Contains("离线", tooltip);
        Assert.Contains("周剩余53%", tooltip);
    }
}

namespace CodexBalanceTray.Tests;

public sealed class TrayStatusEvaluatorTests
{
    [Fact]
    public void GetRemainingPercent_UsesLowestWindow()
    {
        var snapshot = CreateSnapshot(
            new RateLimitWindow(28d, 300, null),
            new RateLimitWindow(59d, 10_080, null));

        Assert.Equal(41d, TrayStatusEvaluator.GetRemainingPercent(snapshot));
    }

    [Fact]
    public void GetRemainingPercent_ReturnsZeroWhenLimitReached()
    {
        var snapshot = CreateSnapshot(
            new RateLimitWindow(28d, 300, null)) with
        {
            RateLimitReachedType = "weekly"
        };

        Assert.Equal(0d, TrayStatusEvaluator.GetRemainingPercent(snapshot));
    }

    [Theory]
    [InlineData(61, "Green")]
    [InlineData(60, "Orange")]
    [InlineData(31, "Orange")]
    [InlineData(30, "Red")]
    public void Evaluate_UsesSpecifiedThresholds(
        double remaining,
        string expected)
    {
        var snapshot = CreateSnapshot(
            new RateLimitWindow(100 - remaining, 300, null));

        Assert.Equal(expected, TrayStatusEvaluator.Evaluate(snapshot).ToString());
    }

    [Fact]
    public void Evaluate_UsesLowestRemainingWindow()
    {
        var snapshot = CreateSnapshot(
            new RateLimitWindow(10, 300, null),
            new RateLimitWindow(72, 10_080, null));

        Assert.Equal(TrayStatusColor.Red, TrayStatusEvaluator.Evaluate(snapshot));
    }

    [Fact]
    public void Evaluate_UsesGrayForStaleOrMissingPercentageData()
    {
        Assert.Equal(TrayStatusColor.Gray, TrayStatusEvaluator.Evaluate(null));
        Assert.Equal(
            TrayStatusColor.Gray,
            TrayStatusEvaluator.Evaluate(CreateSnapshot() with { IsStale = true }));
    }

    [Fact]
    public void Evaluate_ForcesRedWhenLimitWasReached()
    {
        var snapshot = CreateSnapshot(new RateLimitWindow(0, 300, null)) with
        {
            RateLimitReachedType = "primary"
        };

        Assert.Equal(TrayStatusColor.Red, TrayStatusEvaluator.Evaluate(snapshot));
    }

    private static TraySnapshot CreateSnapshot(params RateLimitWindow[] windows) =>
        new(
            windows,
            Credits: null,
            SpendControlReached: false,
            RateLimitReachedType: null,
            PlanType: "plus",
            CapturedAt: DateTimeOffset.Now);
}

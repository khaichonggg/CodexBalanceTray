using System.Text.Json;

namespace CodexBalanceTray.Tests;

public sealed class SnapshotParserTests
{
    [Fact]
    public void ParseRateLimitsResult_ParsesOfficialResponseShape()
    {
        using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "primary": {
              "usedPercent": 47,
              "windowDurationMins": 10080,
              "resetsAt": 1785258687
            },
            "secondary": null,
            "credits": {
              "hasCredits": false,
              "unlimited": false,
              "balance": "0"
            },
            "individualLimit": null,
            "spendControlReached": false,
            "planType": "plus",
            "rateLimitReachedType": null
          }
        }
        """);

        var snapshot = SnapshotParser.ParseRateLimitsResult(document.RootElement);

        Assert.NotNull(snapshot);
        var window = Assert.Single(snapshot.Windows);
        Assert.Equal(53, window.RemainingPercent);
        Assert.Equal(10_080, window.WindowDurationMins);
        Assert.Equal("0", snapshot.Credits?.Balance);
        Assert.Equal("plus", snapshot.PlanType);
    }

    [Fact]
    public void ParseRateLimitsResult_AcceptsSparseUpdateShape()
    {
        using var document = JsonDocument.Parse("""
        {
          "rateLimits": {
            "primary": { "usedPercent": 40, "windowDurationMins": 300 },
            "secondary": { "usedPercent": 70, "windowDurationMins": 10080 }
          }
        }
        """);

        var snapshot = SnapshotParser.ParseRateLimitsResult(document.RootElement);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal(30, snapshot.Windows[1].RemainingPercent);
    }

    [Fact]
    public void RemainingPercent_ClampsInvalidBackendPercentages()
    {
        Assert.Equal(100, new RateLimitWindow(-20, 300, null).RemainingPercent);
        Assert.Equal(0, new RateLimitWindow(120, 300, null).RemainingPercent);
    }
}

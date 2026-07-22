using System.Globalization;

namespace CodexBalanceTray;

internal static class TooltipFormatter
{
    public const int MaximumLength = 127;

    public static string Format(TraySnapshot? snapshot, DateTimeOffset? now = null)
    {
        if (snapshot is null)
        {
            return "Codex｜正在连接…";
        }

        var currentTime = now ?? DateTimeOffset.Now;
        var text = Build(snapshot, includeResetTimes: true, currentTime);
        if (text.Length <= MaximumLength)
        {
            return text;
        }

        text = Build(snapshot, includeResetTimes: false, currentTime);
        return text.Length <= MaximumLength
            ? text
            : text[..MaximumLength];
    }

    private static string Build(
        TraySnapshot snapshot,
        bool includeResetTimes,
        DateTimeOffset now)
    {
        var prefix = snapshot.IsStale
            ? $"Codex(离线{snapshot.CapturedAt.ToLocalTime():HH:mm})"
            : "Codex";

        var segments = new List<string> { prefix };
        foreach (var window in snapshot.Windows.OrderBy(item => item.WindowDurationMins))
        {
            var remaining = Math.Round(
                window.RemainingPercent,
                MidpointRounding.AwayFromZero);
            var segment = $"{GetWindowLabel(window.WindowDurationMins)}剩余{remaining:0}%";
            if (includeResetTimes && window.ResetsAt is long reset)
            {
                segment += $"({FormatReset(reset, window.WindowDurationMins, now)}重置)";
            }

            segments.Add(segment);
        }

        if (snapshot.Credits is { } credits)
        {
            if (credits.Unlimited)
            {
                segments.Add("Credits无限");
            }
            else if (!string.IsNullOrWhiteSpace(credits.Balance))
            {
                segments.Add($"Credits {credits.Balance}");
            }
            else
            {
                segments.Add(credits.HasCredits ? "Credits可用" : "Credits 0");
            }
        }

        if (segments.Count == 1)
        {
            segments.Add("暂无额度数据");
        }

        return string.Join('｜', segments);
    }

    private static string GetWindowLabel(int durationMins)
    {
        if (durationMins is >= 240 and <= 360)
        {
            return "5h";
        }

        if (durationMins is >= 10_000 and <= 10_200)
        {
            return "周";
        }

        return durationMins % 60 == 0
            ? $"{durationMins / 60}h"
            : $"{durationMins}m";
    }

    private static string FormatReset(long unixSeconds, int durationMins, DateTimeOffset now)
    {
        try
        {
            var reset = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            return durationMins >= 1_440 || reset.Date != now.ToLocalTime().Date
                ? reset.ToString("M/d HH:mm", CultureInfo.InvariantCulture)
                : reset.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "未知时间";
        }
    }
}

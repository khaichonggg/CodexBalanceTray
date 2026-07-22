using System.Globalization;
using System.Text.Json;

namespace CodexBalanceTray;

internal static class SnapshotParser
{
    public static TraySnapshot? ParseRateLimitsResult(
        JsonElement payload,
        DateTimeOffset? capturedAt = null)
    {
        if (!TryGetRateLimits(payload, out var rateLimits))
        {
            return null;
        }

        var windows = new List<RateLimitWindow>(capacity: 2);
        AddWindow(rateLimits, "primary", windows);
        AddWindow(rateLimits, "secondary", windows);

        CreditsSnapshot? credits = null;
        if (rateLimits.TryGetProperty("credits", out var creditsElement)
            && creditsElement.ValueKind == JsonValueKind.Object)
        {
            credits = new CreditsSnapshot(
                HasCredits: ReadBoolean(creditsElement, "hasCredits"),
                Unlimited: ReadBoolean(creditsElement, "unlimited"),
                Balance: ReadText(creditsElement, "balance"));
        }

        return new TraySnapshot(
            windows,
            credits,
            SpendControlReached: ReadBoolean(rateLimits, "spendControlReached"),
            RateLimitReachedType: ReadText(rateLimits, "rateLimitReachedType"),
            PlanType: ReadText(rateLimits, "planType"),
            CapturedAt: capturedAt ?? DateTimeOffset.Now);
    }

    private static bool TryGetRateLimits(JsonElement payload, out JsonElement rateLimits)
    {
        if (payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("rateLimits", out rateLimits)
            && rateLimits.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        if (payload.ValueKind == JsonValueKind.Object
            && (payload.TryGetProperty("primary", out _)
                || payload.TryGetProperty("secondary", out _)
                || payload.TryGetProperty("credits", out _)))
        {
            rateLimits = payload;
            return true;
        }

        rateLimits = default;
        return false;
    }

    private static void AddWindow(
        JsonElement rateLimits,
        string propertyName,
        ICollection<RateLimitWindow> windows)
    {
        if (!rateLimits.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("usedPercent", out var usedElement)
            || !usedElement.TryGetDouble(out var usedPercent)
            || !element.TryGetProperty("windowDurationMins", out var durationElement)
            || !durationElement.TryGetInt32(out var durationMins)
            || durationMins <= 0)
        {
            return;
        }

        long? resetsAt = null;
        if (element.TryGetProperty("resetsAt", out var resetElement)
            && resetElement.TryGetInt64(out var resetValue))
        {
            resetsAt = resetValue;
        }

        windows.Add(new RateLimitWindow(usedPercent, durationMins, resetsAt));
    }

    private static bool ReadBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();
    }

    private static string? ReadText(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.TryGetDecimal(out var number)
                ? number.ToString(CultureInfo.InvariantCulture)
                : value.GetRawText(),
            _ => null
        };
    }
}

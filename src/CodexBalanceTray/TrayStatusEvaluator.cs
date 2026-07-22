namespace CodexBalanceTray;

internal static class TrayStatusEvaluator
{
    public static double? GetRemainingPercent(TraySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.IsStale)
        {
            return null;
        }

        if (snapshot.SpendControlReached
            || !string.IsNullOrWhiteSpace(snapshot.RateLimitReachedType))
        {
            return 0d;
        }

        return snapshot.Windows.Count == 0
            ? null
            : snapshot.Windows.Min(window => window.RemainingPercent);
    }

    public static TrayStatusColor Evaluate(TraySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.IsStale)
        {
            return TrayStatusColor.Gray;
        }

        if (snapshot.SpendControlReached
            || !string.IsNullOrWhiteSpace(snapshot.RateLimitReachedType))
        {
            return TrayStatusColor.Red;
        }

        if (snapshot.Windows.Count == 0)
        {
            return TrayStatusColor.Gray;
        }

        var lowestRemaining = GetRemainingPercent(snapshot)!.Value;
        if (lowestRemaining <= 30d)
        {
            return TrayStatusColor.Red;
        }

        return lowestRemaining <= 60d
            ? TrayStatusColor.Orange
            : TrayStatusColor.Green;
    }
}

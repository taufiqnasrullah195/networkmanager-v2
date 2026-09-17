using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Persistence;

namespace NETworkManager.AI.Dashboard;

/// <summary>
///     Pure, deterministic calculations for the dashboard: availability, staleness, and latency. No I/O, no inference.
/// </summary>
public static class DashboardCalculators
{
    /// <summary>
    ///     Availability = healthy observations / total valid observations over the window. UNKNOWN observations are
    ///     excluded from the denominator; Timeout/Error/Cancelled count as non-healthy (they are real observations).
    ///     This is availability of observations, NOT an SLA — present it as such.
    /// </summary>
    public static (double? Percent, int Healthy, int Total) ComputeAvailability(
        IEnumerable<MonitoringHistoryEntry> entries, DateTimeOffset windowStart)
    {
        var healthy = 0;
        var total = 0;

        foreach (var entry in entries)
        {
            if (entry.Timestamp < windowStart)
                continue;

            if (entry.Status == MonitoringResultStatus.Unknown)
                continue;

            total++;
            if (entry.Status == MonitoringResultStatus.Healthy)
                healthy++;
        }

        if (total == 0)
            return (null, 0, 0);

        return (healthy * 100.0 / total, healthy, total);
    }

    /// <summary>A target is stale when its last check is null or older than the threshold (missed intervals).</summary>
    public static bool IsStale(DateTimeOffset? lastCheck, DateTimeOffset now, TimeSpan threshold)
    {
        if (lastCheck is null)
            return true;

        return now - lastCheck.Value > threshold;
    }

    /// <summary>Latest ping observation for a snapshot, or null when no ping result is present.</summary>
    public static PingResult? LatestPing(MonitoringSnapshot snapshot) =>
        snapshot.Results
            .Where(r => r.Observed is PingResult)
            .Select(r => (PingResult)r.Observed!)
            .LastOrDefault();

    /// <summary>Maps an aggregated health status to a coarse trend bucket (for the state timeline).</summary>
    public static TrendHealth ToTrendHealth(NetworkHealthStatus status) => status switch
    {
        NetworkHealthStatus.Healthy => TrendHealth.Healthy,
        NetworkHealthStatus.Degraded => TrendHealth.Degraded,
        NetworkHealthStatus.Unhealthy => TrendHealth.Unhealthy,
        _ => TrendHealth.Unknown,
    };
}

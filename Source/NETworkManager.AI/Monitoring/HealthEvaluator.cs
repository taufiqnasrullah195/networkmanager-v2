using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     Deterministic health interpretation: a result maps to a health contribution, and a target's health is the
///     worst of its checks. No inference beyond the measurements — a timeout or cancelled run contributes no firm
///     claim (Unknown/Degraded), never a fabricated "down".
/// </summary>
public sealed class HealthEvaluator : IHealthEvaluator
{
    public NetworkHealthStatus Evaluate(MonitoringResult result) => result.Status switch
    {
        MonitoringResultStatus.Healthy => NetworkHealthStatus.Healthy,
        MonitoringResultStatus.Warning => NetworkHealthStatus.Degraded,
        MonitoringResultStatus.Timeout => NetworkHealthStatus.Degraded,
        MonitoringResultStatus.Unhealthy => NetworkHealthStatus.Unhealthy,
        MonitoringResultStatus.Error => NetworkHealthStatus.Unhealthy,
        // Unknown / Running / Cancelled carry no usable evidence about the target.
        _ => NetworkHealthStatus.Unknown,
    };

    public NetworkHealthStatus Evaluate(IReadOnlyList<MonitoringResult> results)
    {
        if (results.Count == 0)
            return NetworkHealthStatus.Unknown;

        var contributions = results.Select(Evaluate).Where(s => s != NetworkHealthStatus.Unknown).ToList();

        if (contributions.Count == 0)
            return NetworkHealthStatus.Unknown;

        if (contributions.Contains(NetworkHealthStatus.Unhealthy))
            return NetworkHealthStatus.Unhealthy;

        if (contributions.Contains(NetworkHealthStatus.Degraded))
            return NetworkHealthStatus.Degraded;

        return NetworkHealthStatus.Healthy;
    }
}
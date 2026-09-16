using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>Deterministic interpretation of monitoring results into a target health status. No inference beyond the measurements.</summary>
public interface IHealthEvaluator
{
    /// <summary>Health contribution of a single result.</summary>
    NetworkHealthStatus Evaluate(MonitoringResult result);

    /// <summary>Aggregated health of a target from its latest results.</summary>
    NetworkHealthStatus Evaluate(IReadOnlyList<MonitoringResult> results);
}
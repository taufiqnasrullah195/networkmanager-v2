using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>Read-only view of monitoring state exposed to the AI tool. Cannot modify targets or configuration.</summary>
public interface IMonitoringQuery
{
    /// <summary>Current health + latest results for every monitored target.</summary>
    IReadOnlyList<MonitoringSnapshot> GetCurrentStatus();

    /// <summary>Aggregated health for one target, or <c>null</c> if the target is unknown.</summary>
    NetworkHealthStatus? GetHealth(string targetId);

    /// <summary>Latest result for one target/check-type, or <c>null</c> if none recorded.</summary>
    MonitoringResult? GetLatest(string targetId, MonitorCheckType type);

    /// <summary>Recent non-healthy results (most recent first), useful for "show recent monitoring failures".</summary>
    IReadOnlyList<MonitoringResult> GetRecentFailures(int maxCount);
}
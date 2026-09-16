using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     In-memory state store for the current monitoring state (latest results + per-target health). Interface is
///     deliberately persistence-agnostic so a future durable store can replace it without touching the engine.
/// </summary>
public interface IMonitoringStateStore
{
    /// <summary>Records the latest result for a target/check-type (replaces the previous one).</summary>
    void Record(MonitoringResult result);

    /// <summary>Records the aggregated health of a target at a point in time.</summary>
    void SetHealth(string targetId, NetworkHealthStatus health, DateTimeOffset timestamp);

    NetworkHealthStatus GetHealth(string targetId);

    /// <summary>Latest result per check-type for a target (health evaluator input).</summary>
    IReadOnlyList<MonitoringResult> GetLatestResults(string targetId);

    /// <summary>Most recent non-healthy results, most recent first, up to <paramref name="maxCount"/>.</summary>
    IReadOnlyList<MonitoringResult> GetRecentFailures(int maxCount);

    /// <summary>Removes all state for a target (used when a target is removed).</summary>
    void RemoveTarget(string targetId);

    /// <summary>Discards all recorded state.</summary>
    void Clear();
}
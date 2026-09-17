using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Lifecycle and query surface of the monitoring engine. Monitoring is always read-only at the check level;
///     target/check management is exposed only to the host application, never to the AI tool layer.
/// </summary>
public interface IMonitoringEngine : IMonitoringQuery
{
    /// <summary>Starts periodic monitoring. Idempotent; a second call while running is a no-op.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Gracefully stops periodic monitoring and waits for in-flight checks to finish (bounded).</summary>
    Task StopAsync();

    /// <summary>Adds a target. Disabled targets are retained but not scheduled.</summary>
    void AddTarget(MonitoringTarget target);

    /// <summary>Removes a target and all of its checks/state.</summary>
    bool RemoveTarget(string targetId);

    /// <summary>Enables or disables a target (toggles all of its checks together).</summary>
    void SetTargetEnabled(string targetId, bool enabled);

    /// <summary>Adds/schedules a check. The referenced target must already exist.</summary>
    void AddCheck(MonitoringCheck check);

    /// <summary>Removes a check (must be stopped or removed while not in the middle of a run — safe to call anytime).</summary>
    bool RemoveCheck(string checkId);

    /// <summary>
    ///     Removes all targets, checks, and recorded state. Safe to call while stopped (the configuration applier
    ///     clears before re-applying a profile set).
    /// </summary>
    void Clear();

    /// <summary>True while the periodic loop is running.</summary>
    bool IsRunning { get; }

    /// <summary>Runs a single check immediately (outside the periodic loop) and records its result.</summary>
    Task<MonitoringResult> RunCheckAsync(MonitoringCheck check, CancellationToken cancellationToken = default);

    /// <summary>Registers an observer for lifecycle/transition events.</summary>
    void Subscribe(IMonitoringObserver observer);

    void Unsubscribe(IMonitoringObserver observer);
}
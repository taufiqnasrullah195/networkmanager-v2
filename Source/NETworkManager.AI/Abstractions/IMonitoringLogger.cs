using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>Lifecycle/transition logging for monitoring. Takes only ids, statuses, and safe messages — never secrets.</summary>
public interface IMonitoringLogger
{
    void MonitoringStarted();

    void MonitoringStopped();

    void TargetAdded(string targetId);

    void TargetRemoved(string targetId);

    void TargetEnabled(string targetId, bool enabled);

    void CheckScheduled(string checkId, string targetId);

    void HealthStateChanged(HealthStateChange change);

    void MonitoringError(string checkId, string message);
}
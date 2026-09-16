using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>A silent logger — the monitoring default when no host logger is wired in.</summary>
public sealed class NullMonitoringLogger : IMonitoringLogger
{
    public void MonitoringStarted() { }
    public void MonitoringStopped() { }
    public void TargetAdded(string targetId) { }
    public void TargetRemoved(string targetId) { }
    public void TargetEnabled(string targetId, bool enabled) { }
    public void CheckScheduled(string checkId, string targetId) { }
    public void HealthStateChanged(HealthStateChange change) { }
    public void MonitoringError(string checkId, string message) { }
}
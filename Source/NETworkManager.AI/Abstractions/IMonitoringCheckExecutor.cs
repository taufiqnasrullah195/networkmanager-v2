using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>Executes one read-only monitoring check against a target and returns a structured result. Never throws for expected failures.</summary>
public interface IMonitoringCheckExecutor
{
    Task<MonitoringResult> ExecuteAsync(MonitoringCheck check, MonitoringTarget target, CancellationToken cancellationToken = default);
}
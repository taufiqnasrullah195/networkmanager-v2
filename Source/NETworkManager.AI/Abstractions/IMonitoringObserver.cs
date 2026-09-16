using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>An observer of monitoring events (lifecycle + health-state changes). No framework; UI/alerting subscribe directly.</summary>
public interface IMonitoringObserver
{
    void OnMonitoringEvent(MonitoringEvent e);
}
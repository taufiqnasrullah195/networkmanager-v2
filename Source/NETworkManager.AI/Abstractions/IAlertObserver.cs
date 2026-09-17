using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Abstractions;

/// <summary>Receives alert lifecycle events (created/updated/acknowledged/resolved).</summary>
public interface IAlertObserver
{
    void OnAlertEvent(AlertEvent e);
}
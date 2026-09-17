using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Abstractions;

/// <summary>Alert lifecycle logging (ids/statuses/severity only — never credentials).</summary>
public interface IAlertLogger
{
    void AlertCreated(string alertId, AlertSeverity severity);

    void AlertUpdated(string alertId);

    void AlertAcknowledged(string alertId);

    void AlertResolved(string alertId);

    void AlertRuleError(string message);
}
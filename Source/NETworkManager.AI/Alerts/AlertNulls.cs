using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Alerts;

/// <summary>A silent alert logger — the default when no host logger is wired in.</summary>
public sealed class NullAlertLogger : IAlertLogger
{
    public void AlertCreated(string alertId, AlertSeverity severity) { }
    public void AlertUpdated(string alertId) { }
    public void AlertAcknowledged(string alertId) { }
    public void AlertResolved(string alertId) { }
    public void AlertRuleError(string message) { }
}

/// <summary>A no-op suppression policy (real maintenance windows/muted targets land in a future step).</summary>
public sealed class NullAlertSuppressionPolicy : IAlertSuppressionPolicy
{
    public bool IsSuppressed(HealthStateChange change) => false;
}
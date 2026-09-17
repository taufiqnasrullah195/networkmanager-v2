using System;
using log4net;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;

namespace NETworkManager;

/// <summary>Bridges the alert engine's lifecycle logger to log4net (ids/severity only — no credentials).</summary>
public sealed class Log4netAlertLogger : IAlertLogger
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AlertEngine));

    public void AlertCreated(string alertId, AlertSeverity severity) => Log.Info($"Alert created '{alertId}' ({severity}).");
    public void AlertUpdated(string alertId) => Log.Info($"Alert updated '{alertId}'.");
    public void AlertAcknowledged(string alertId) => Log.Info($"Alert acknowledged '{alertId}'.");
    public void AlertResolved(string alertId) => Log.Info($"Alert resolved '{alertId}'.");
    public void AlertRuleError(string message) => Log.Warn($"Alert rule error: {message}");
}

/// <summary>
///     Composition root for the alert engine (Step 11). A single <see cref="AlertEngine"/> consumes monitoring
///     health-state changes; it is attached to the (rebuilt) monitoring engine when monitoring starts, and its
///     read-only surface backs both the WPF alert panel and the AI <c>network_alerts</c> tool.
/// </summary>
public static class AlertComposition
{
    private static readonly Lazy<AlertEngine> AlertsInstance = new(CreateAlerts);

    public static AlertEngine Alerts => AlertsInstance.Value;

    private static AlertEngine CreateAlerts() =>
        new(logger: new Log4netAlertLogger());
}
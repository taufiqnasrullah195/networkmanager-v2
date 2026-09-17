using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Alerts;

/// <summary>
///     Deterministic alert rules. Severity comes from the state transition, never from an AI. CRITICAL is reserved
///     for a future explicit rule (none in this step). Transitions from UNKNOWN are ignored (no healthy baseline).
/// </summary>
public sealed class AlertEvaluator : IAlertEvaluator
{
    private readonly AlertOptions _options;

    public AlertEvaluator(AlertOptions? options = null)
    {
        _options = options ?? new AlertOptions();
    }

    public AlertDecision Evaluate(HealthStateChange change)
    {
        if (!_options.AlertingEnabled)
            return Ignore("alerting-disabled");

        return (change.Previous, change.New) switch
        {
            (NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy) =>
                _options.CreateAlertOnUnhealthy ? Create(AlertSeverity.Error, "healthy-to-unhealthy") : Ignore("unhealthy-disabled"),

            (NetworkHealthStatus.Healthy, NetworkHealthStatus.Degraded) =>
                _options.CreateAlertOnDegraded ? Create(AlertSeverity.Warning, "healthy-to-degraded") : Ignore("degraded-disabled"),

            // Escalation / downgrade: deduplicated into an update of the active alert (severity change).
            (NetworkHealthStatus.Degraded, NetworkHealthStatus.Unhealthy) => Create(AlertSeverity.Error, "degraded-to-unhealthy"),
            (NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Degraded) => Create(AlertSeverity.Warning, "unhealthy-to-degraded"),

            (NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy) =>
                _options.AutoResolveOnRecovery ? Resolve("unhealthy-to-healthy") : Ignore("auto-resolve-disabled"),

            (NetworkHealthStatus.Degraded, NetworkHealthStatus.Healthy) =>
                _options.AutoResolveOnRecovery ? Resolve("degraded-to-healthy") : Ignore("auto-resolve-disabled"),

            _ => Ignore("no-rule"),
        };
    }

    private static AlertDecision Create(AlertSeverity severity, string ruleId) => new()
    {
        Kind = AlertDecisionKind.Create,
        Severity = severity,
        RuleId = ruleId,
    };

    private static AlertDecision Resolve(string ruleId) => new()
    {
        Kind = AlertDecisionKind.Resolve,
        RuleId = ruleId,
    };

    private static AlertDecision Ignore(string ruleId) => new()
    {
        Kind = AlertDecisionKind.Ignore,
        RuleId = ruleId,
    };
}
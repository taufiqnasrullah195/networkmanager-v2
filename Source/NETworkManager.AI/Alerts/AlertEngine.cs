using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Alerts;

/// <summary>
///     Consumes monitoring events and produces structured alerts. Deterministic: evaluates rules, deduplicates by
///     fingerprint, creates/updates/resolves, publishes lifecycle events. No AI calls, no remediation, no secrets.
/// </summary>
public sealed class AlertEngine : IAlertEngine, IMonitoringObserver
{
    private readonly AlertOptions _options;
    private readonly IAlertStore _store;
    private readonly IAlertEvaluator _evaluator;
    private readonly IAlertSuppressionPolicy _suppression;
    private readonly IAlertLogger _logger;

    private readonly object _gate = new();
    private readonly object _mutateGate = new();
    private readonly List<IAlertObserver> _observers = new();
    private bool _running;

    public AlertEngine(
        AlertOptions? options = null,
        IAlertEvaluator? evaluator = null,
        IAlertStore? store = null,
        IAlertSuppressionPolicy? suppression = null,
        IAlertLogger? logger = null)
    {
        _options = options ?? new AlertOptions();
        _store = store ?? new AlertStore();
        _evaluator = evaluator ?? new AlertEvaluator(_options);
        _suppression = suppression ?? new NullAlertSuppressionPolicy();
        _logger = logger ?? new NullAlertLogger();
    }

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public void Start()
    {
        lock (_gate)
            _running = true;
    }

    public void Stop()
    {
        lock (_gate)
            _running = false;
    }

    public void ProcessHealthStateChanged(HealthStateChange change)
    {
        if (!IsRunning)
            return;

        ArgumentNullException.ThrowIfNull(change);

        try
        {
            var decision = _evaluator.Evaluate(change);

            if (decision.Kind == AlertDecisionKind.Ignore)
                return;

            if (_suppression.IsSuppressed(change))
                return;

            lock (_mutateGate)
            {
                switch (decision.Kind)
                {
                    case AlertDecisionKind.Create:
                        CreateOrUpdate(change, decision);
                        break;
                    case AlertDecisionKind.Resolve:
                        ResolveTarget(change);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.AlertRuleError(ex.Message);
        }
    }

    /// <summary>Routes monitoring events: state changes become alerts; recurring failures update occurrence metadata.</summary>
    public void OnMonitoringEvent(MonitoringEvent e)
    {
        if (!IsRunning)
            return;

        if (e.Type == MonitoringEventType.HealthStateChanged && e.StateChange is not null)
            ProcessHealthStateChanged(e.StateChange);
        else if (e.Type == MonitoringEventType.MonitoringCompleted && e.Result is not null)
            ProcessRecurringFailure(e.Result);
    }

    public void ProcessRecurringFailure(MonitoringResult result)
    {
        if (!IsRunning || !_options.DeduplicationEnabled)
            return;

        if (result.Status is not (MonitoringResultStatus.Unhealthy or MonitoringResultStatus.Timeout or MonitoringResultStatus.Error))
            return;

        var fingerprint = Fingerprint(result.TargetId, result.CheckType.ToString());

        lock (_mutateGate)
        {
            var active = _store.GetActiveAlerts()
                .FirstOrDefault(a => string.Equals(a.Fingerprint, fingerprint, StringComparison.Ordinal));

            if (active is null)
                return;

            var updated = active with
            {
                OccurrenceCount = active.OccurrenceCount + 1,
                LastSeenAt = result.Timestamp,
                Reason = result.SafeMessage,
                Evidence = DescribeEvidence(result),
            };

            _store.UpdateAlert(updated);
            _logger.AlertUpdated(updated.AlertId);
            Publish(new AlertEvent { Type = AlertEventType.AlertUpdated, Alert = updated, Timestamp = result.Timestamp });
        }
    }

    // ----- read (IAlertQuery) ----------------------------------------------

    public IReadOnlyList<Alert> GetActiveAlerts() => _store.GetActiveAlerts();

    public Alert? GetAlert(string alertId) => _store.GetAlert(alertId);

    public IReadOnlyList<Alert> GetRecentAlerts(int maxCount) => _store.GetRecentAlerts(maxCount);

    // ----- write (UI only) -------------------------------------------------

    public bool AcknowledgeAlert(string alertId)
    {
        lock (_mutateGate)
        {
            var alert = _store.GetAlert(alertId);

            if (alert is null || alert.Status != AlertStatus.Open)
                return false;

            var updated = alert with { Status = AlertStatus.Acknowledged, AcknowledgedAt = DateTimeOffset.UtcNow };

            _store.UpdateAlert(updated);
            _logger.AlertAcknowledged(alertId);
            Publish(new AlertEvent { Type = AlertEventType.AlertAcknowledged, Alert = updated, Timestamp = DateTimeOffset.UtcNow });

            return true;
        }
    }

    public bool ResolveAlert(string alertId, string? resolutionEvidence = null)
    {
        lock (_mutateGate)
        {
            var alert = _store.GetAlert(alertId);

            if (alert is null || alert.Status == AlertStatus.Resolved)
                return false;

            var updated = alert with
            {
                Status = AlertStatus.Resolved,
                ResolvedAt = DateTimeOffset.UtcNow,
                ResolutionEvidence = resolutionEvidence,
            };

            _store.UpdateAlert(updated);
            _logger.AlertResolved(alertId);
            Publish(new AlertEvent { Type = AlertEventType.AlertResolved, Alert = updated, Timestamp = DateTimeOffset.UtcNow });

            return true;
        }
    }

    // ----- observers -------------------------------------------------------

    public void Subscribe(IAlertObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
            _observers.Add(observer);
    }

    public void Unsubscribe(IAlertObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
            _observers.Remove(observer);
    }

    // ----- internal --------------------------------------------------------

    private void CreateOrUpdate(HealthStateChange change, AlertDecision decision)
    {
        var checkType = change.RelatedResult?.CheckType.ToString() ?? "unknown";
        var fingerprint = Fingerprint(change.TargetId, checkType);

        var active = _store.GetActiveAlerts()
            .FirstOrDefault(a => string.Equals(a.Fingerprint, fingerprint, StringComparison.Ordinal));

        if (active is not null)
        {
            var updated = active with
            {
                Severity = decision.Severity,
                LastSeenAt = change.Timestamp,
                CurrentHealthState = change.New,
                Reason = change.Reason,
                Evidence = DescribeEvidence(change.RelatedResult),
            };

            _store.UpdateAlert(updated);
            _logger.AlertUpdated(updated.AlertId);
            Publish(new AlertEvent { Type = AlertEventType.AlertUpdated, Alert = updated, Timestamp = change.Timestamp });
            return;
        }

        var alert = new Alert
        {
            AlertId = Guid.NewGuid().ToString("N"),
            Fingerprint = fingerprint,
            TargetId = change.TargetId,
            TargetName = change.DisplayName,
            Severity = decision.Severity,
            Status = AlertStatus.Open,
            Title = BuildTitle(change),
            Description = BuildDescription(change),
            Reason = change.Reason,
            Evidence = DescribeEvidence(change.RelatedResult),
            FirstSeenAt = change.Timestamp,
            LastSeenAt = change.Timestamp,
            OccurrenceCount = 1,
            PreviousHealthState = change.Previous,
            CurrentHealthState = change.New,
            FailureClassification = change.RelatedResult?.ErrorClassification.ToString(),
        };

        _store.CreateAlert(alert);
        _logger.AlertCreated(alert.AlertId, alert.Severity);
        Publish(new AlertEvent { Type = AlertEventType.AlertCreated, Alert = alert, Timestamp = change.Timestamp });
    }

    private void ResolveTarget(HealthStateChange change)
    {
        var active = _store.GetActiveAlerts()
            .Where(a => string.Equals(a.TargetId, change.TargetId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var alert in active)
        {
            var resolved = alert with
            {
                Status = AlertStatus.Resolved,
                ResolvedAt = change.Timestamp,
                ResolutionEvidence = change.Reason,
                LastSeenAt = change.Timestamp,
            };

            _store.UpdateAlert(resolved);
            _logger.AlertResolved(resolved.AlertId);
            Publish(new AlertEvent { Type = AlertEventType.AlertResolved, Alert = resolved, Timestamp = change.Timestamp });
        }
    }

    private void Publish(AlertEvent e)
    {
        IAlertObserver[] snapshot;

        lock (_gate)
            snapshot = _observers.ToArray();

        foreach (var observer in snapshot)
        {
            try
            {
                observer.OnAlertEvent(e);
            }
            catch (Exception ex)
            {
                _logger.AlertRuleError($"Alert observer threw: {ex.Message}");
            }
        }
    }

    private static string Fingerprint(string targetId, string checkType) =>
        $"{targetId}\u0001{checkType}";

    private static string BuildTitle(HealthStateChange change) => change.New switch
    {
        NetworkHealthStatus.Unhealthy => $"{change.DisplayName} became unhealthy.",
        NetworkHealthStatus.Degraded => $"{change.DisplayName} became degraded.",
        NetworkHealthStatus.Healthy => $"{change.DisplayName} recovered.",
        _ => $"{change.DisplayName} health changed.",
    };

    private static string? BuildDescription(HealthStateChange change) =>
        $"Health changed from {change.Previous} to {change.New}.";

    private static string? DescribeEvidence(MonitoringResult? result)
    {
        if (result is null)
            return null;

        return result.Observed switch
        {
            PingResult ping => $"{ping.Sent} probes, {ping.Received} successful, {ping.TimedOutCount} timed out",
            TcpTestResult tcp => $"TCP {tcp.Port}: {tcp.State}",
            DnsLookupResult dns => dns.Success
                ? $"DNS resolved: {string.Join(", ", dns.Records.Where(r => r.RecordType is "A" or "AAAA").Select(r => r.Result))}"
                : "DNS resolution failed",
            _ => result.SafeMessage,
        };
    }
}
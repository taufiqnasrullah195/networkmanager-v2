using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Notifications;

/// <summary>
///     Consumes alert lifecycle events and turns them into notifications through the policy + service. An ALERT is a
///     network condition; a NOTIFICATION is a delivery attempt. Monitoring continues regardless of notification
///     success/failure, and a FAILED delivery is never interpreted as recovery. Idempotency (per alert+event+level),
///     cooldown, escalation, and recovery are all deterministic and secret-free.
/// </summary>
public sealed class NotificationEngine : IAlertObserver
{
    private readonly INotificationService _service;
    private readonly INotificationPolicy _policy;
    private readonly INotificationStore _store;
    private readonly IAlertQuery _alerts;
    private readonly NotificationEngineOptions _options;
    private readonly INotificationLogger _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _notifiedEvents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _lastNotified = new(StringComparer.Ordinal);

    public NotificationEngine(
        INotificationService service,
        INotificationPolicy policy,
        INotificationStore store,
        IAlertQuery alerts,
        NotificationEngineOptions? options = null,
        INotificationLogger? logger = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        _options = options ?? new NotificationEngineOptions();
        _logger = logger ?? new NullNotificationLogger();
    }

    public NotificationEngineOptions Options => _options;

    /// <summary>Fire-and-forget observer hook (the alert engine's publish path is synchronous).</summary>
    public void OnAlertEvent(AlertEvent e) => _ = ProcessAlertEventAsync(e, DateTimeOffset.UtcNow, CancellationToken.None);

    /// <summary>Deterministic processing of one alert event (testable).</summary>
    public Task<IReadOnlyList<Notification>> ProcessAlertEventAsync(AlertEvent e, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(e);

        var eventType = e.Type switch
        {
            AlertEventType.AlertCreated => NotificationEventType.AlertCreated,
            AlertEventType.AlertResolved => NotificationEventType.AlertRecovered,
            _ => (NotificationEventType?)null,
        };

        if (eventType is null)
            return Task.FromResult<IReadOnlyList<Notification>>(Array.Empty<Notification>());

        return NotifyAsync(e.Alert, eventType.Value, 0, now, cancellationToken);
    }

    /// <summary>
    ///     Evaluates escalation rules against currently-active alerts and sends escalation notifications (once per level).
    ///     Call periodically (e.g. every minute); it is deterministic given <paramref name="now"/>.
    /// </summary>
    public async Task<IReadOnlyList<Notification>> EvaluateEscalationAsync(DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var result = new List<Notification>();

        foreach (var alert in _alerts.GetActiveAlerts())
        {
            foreach (var rule in _options.EscalationRules.Where(r => r.Enabled).OrderBy(r => r.Level))
            {
                if (now - alert.FirstSeenAt < rule.AfterDuration)
                    continue;

                result.AddRange(await NotifyAsync(alert, NotificationEventType.AlertEscalated, rule.Level, now,
                    cancellationToken, rule.EscalatedSeverity).ConfigureAwait(false));
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<Notification>> NotifyAsync(Alert alert, NotificationEventType eventType, int level,
        DateTimeOffset now, CancellationToken cancellationToken, AlertSeverity? severityOverride = null)
    {
        // Escalation uses the escalated severity for both the policy check and the payload.
        var effectiveAlert = severityOverride is null ? alert : alert with { Severity = severityOverride.Value };

        var decision = _policy.Decide(effectiveAlert, eventType, level);
        if (!decision.ShouldNotify)
            return Array.Empty<Notification>();

        var key = IdempotencyKey(alert.AlertId, eventType, level);

        lock (_gate)
        {
            if (_notifiedEvents.Contains(key))
                return Array.Empty<Notification>();
        }

        if (await _store.HasSentIdempotencyKeyAsync(key, cancellationToken).ConfigureAwait(false))
        {
            lock (_gate)
                _notifiedEvents.Add(key);

            return Array.Empty<Notification>();
        }

        // Cooldown (non-recovery): do not re-notify the same alert within the cooldown window.
        if (eventType != NotificationEventType.AlertRecovered && _options.Cooldown > TimeSpan.Zero)
        {
            lock (_gate)
            {
                if (_lastNotified.TryGetValue(alert.AlertId, out var last) && now - last < _options.Cooldown)
                {
                    _logger.SuppressedByCooldown(alert.AlertId);
                    return Array.Empty<Notification>();
                }
            }
        }

        var request = BuildRequest(effectiveAlert, eventType, level, now);
        var notifications = await _service.SendAsync(request, decision.Channels, cancellationToken).ConfigureAwait(false);

        if (notifications.Any(n => n.Status == NotificationStatus.Sent))
        {
            lock (_gate)
            {
                _notifiedEvents.Add(key);
                _lastNotified[alert.AlertId] = now;
            }

            switch (eventType)
            {
                case NotificationEventType.AlertRecovered:
                    _logger.RecoverySent(alert.AlertId);
                    break;
                case NotificationEventType.AlertEscalated:
                    _logger.Escalated(alert.AlertId, level);
                    break;
            }
        }

        return notifications;
    }

    private static NotificationRequest BuildRequest(Alert alert, NotificationEventType eventType, int level, DateTimeOffset now)
    {
        var severity = alert.Severity.ToString();

        var title = eventType switch
        {
            NotificationEventType.AlertCreated => alert.Title,
            NotificationEventType.AlertEscalated => $"{alert.Title} (escalated)",
            NotificationEventType.AlertRecovered => $"{alert.TargetName} recovered.",
            _ => alert.Title,
        };

        var message = eventType switch
        {
            NotificationEventType.AlertCreated => $"Target: {alert.TargetName}\nSeverity: {severity}",
            NotificationEventType.AlertEscalated => $"Target: {alert.TargetName}\nEscalated to {severity}",
            NotificationEventType.AlertRecovered => $"Target: {alert.TargetName}\nPrevious issue: {alert.Title}",
            _ => string.Empty,
        };

        return new NotificationRequest
        {
            NotificationId = Guid.NewGuid().ToString("N"),
            AlertId = alert.AlertId,
            Title = title,
            Message = message,
            Severity = severity,
            Target = alert.TargetName,
            Timestamp = now,
            Reason = alert.Reason,
            EvidenceSummary = alert.Evidence,
            EventType = eventType,
            IdempotencyKey = IdempotencyKey(alert.AlertId, eventType, level),
        };
    }

    private static string IdempotencyKey(string alertId, NotificationEventType eventType, int level) =>
        $"{alertId}:{eventType}:{level}";
}

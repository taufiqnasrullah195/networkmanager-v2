using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Notifications;

/// <summary>
///     Deterministic notification policy: severity filter, event-type filter, and channel routing. Cooldown/escalation/
///     grouping live in the engine; this decides WHETHER and WHERE for a single alert event.
/// </summary>
public sealed class NotificationPolicy : INotificationPolicy
{
    private readonly NotificationPolicyConfig _config;

    public NotificationPolicy(NotificationPolicyConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public NotificationPolicyConfig Config => _config;

    public NotificationDecision Decide(Alert alert, NotificationEventType eventType, int escalationLevel)
    {
        if (!_config.Enabled)
            return No("notifications are disabled");

        if (eventType == NotificationEventType.AlertCreated && !_config.NotifyOnCreated)
            return No("created notifications are disabled");

        if (eventType == NotificationEventType.AlertRecovered && !_config.NotifyOnRecovery)
            return No("recovery notifications are disabled");

        if (eventType == NotificationEventType.AlertEscalated && !_config.NotifyOnEscalation)
            return No("escalation notifications are disabled");

        if (alert.Severity < _config.MinimumSeverity)
            return No($"alert severity {alert.Severity} is below the minimum {_config.MinimumSeverity}");

        var channels = _config.ChannelRouting.TryGetValue(alert.Severity.ToString(), out var routed)
            ? routed
            : _config.DefaultChannels;

        return new NotificationDecision { ShouldNotify = true, Channels = channels };
    }

    private static NotificationDecision No(string reason) => new()
    {
        ShouldNotify = false,
        SuppressionReason = reason,
    };
}

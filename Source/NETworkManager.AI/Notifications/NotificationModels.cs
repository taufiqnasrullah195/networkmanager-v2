using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Notifications;

/// <summary>Lifecycle of one notification delivery attempt (distinct from alert lifecycle).</summary>
public enum NotificationStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Cancelled = 3,
}

/// <summary>What kind of alert event this notification delivers. Independent of alert lifecycle.</summary>
public enum NotificationEventType
{
    AlertCreated = 0,
    AlertEscalated = 1,
    AlertRecovered = 2,
}

/// <summary>One persisted notification delivery record. Secret-free; a FAILED delivery is never an alert resolution.</summary>
public sealed record Notification
{
    public required string NotificationId { get; init; }
    public required string AlertId { get; init; }
    public required string Channel { get; init; }
    public required NotificationStatus Status { get; init; }
    public required NotificationEventType EventType { get; init; }
    public required string IdempotencyKey { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? SentAt { get; init; }
    public int AttemptCount { get; init; }
    public string? FailureReason { get; init; }
}

/// <summary>Provider-neutral, secret-free notification payload passed to a channel.</summary>
public sealed record NotificationRequest
{
    public required string NotificationId { get; init; }
    public required string AlertId { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public required string Target { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? Reason { get; init; }
    public string? EvidenceSummary { get; init; }
    public required NotificationEventType EventType { get; init; }
    public required string IdempotencyKey { get; init; }
}

/// <summary>Static metadata about a notification channel (for settings UI / status display). No secrets.</summary>
public sealed record NotificationChannelMetadata(
    string Name,
    string Description,
    bool RequiresCredential,
    bool IsAvailable,
    bool IsConfigured);

/// <summary>The policy's decision for one alert event: notify (to which channels) or suppress (why).</summary>
public sealed record NotificationDecision
{
    public bool ShouldNotify { get; init; }
    public IReadOnlyList<string> Channels { get; init; } = Array.Empty<string>();
    public string? SuppressionReason { get; init; }
}

/// <summary>A deterministic escalation rule: after an alert is active this long, escalate to a higher severity.</summary>
public sealed record AlertEscalationRule
{
    public int Level { get; init; } = 1;

    public TimeSpan AfterDuration { get; init; }

    public AlertSeverity EscalatedSeverity { get; init; } = AlertSeverity.Error;

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Level < 1)
            errors.Add("Escalation level must be >= 1.");
        if (AfterDuration <= TimeSpan.Zero)
            errors.Add("Escalation duration must be positive.");
        return errors;
    }
}

/// <summary>User-controlled notification policy configuration. No secrets.</summary>
public sealed record NotificationPolicyConfig
{
    public bool Enabled { get; init; } = true;

    public AlertSeverity MinimumSeverity { get; init; } = AlertSeverity.Warning;

    public bool NotifyOnCreated { get; init; } = true;

    public bool NotifyOnRecovery { get; init; } = true;

    public bool NotifyOnEscalation { get; init; } = true;

    public IReadOnlyList<string> DefaultChannels { get; init; } = new[] { "Desktop" };

    /// <summary>Severity-name → channel names. Falls back to <see cref="DefaultChannels"/>.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ChannelRouting { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (DefaultChannels.Count == 0)
            errors.Add("At least one default notification channel must be configured.");
        return errors;
    }
}

/// <summary>Engine-level timing controls (cooldown, escalation, grouping).</summary>
public sealed record NotificationEngineOptions
{
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromMinutes(15);

    public IReadOnlyList<AlertEscalationRule> EscalationRules { get; init; } = Array.Empty<AlertEscalationRule>();

    public bool GroupingEnabled { get; init; }

    public TimeSpan GroupingWindow { get; init; } = TimeSpan.FromSeconds(30);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Cooldown < TimeSpan.Zero)
            errors.Add("Cooldown must not be negative.");
        foreach (var rule in EscalationRules)
            errors.AddRange(rule.Validate().Select(e => $"Escalation rule {rule.Level}: {e}"));
        return errors;
    }
}

/// <summary>Bounded retry behavior for a failing channel.</summary>
public sealed record NotificationRetryOptions
{
    public int MaxAttempts { get; init; } = 3;

    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    public bool ExponentialBackoff { get; init; } = true;
}

/// <summary>Observability for notification delivery (ids/status only — no secrets).</summary>
public interface INotificationLogger
{
    void Queued(string notificationId, string alertId);
    void Sent(string notificationId, string channel);
    void Failed(string notificationId, string channel, string reason);
    void Retry(string notificationId, string channel, int attempt);
    void SuppressedByCooldown(string alertId);
    void Escalated(string alertId, int level);
    void RecoverySent(string alertId);
    void Grouped(int count);
}

/// <summary>No-op logger (default).</summary>
public sealed class NullNotificationLogger : INotificationLogger
{
    public void Queued(string notificationId, string alertId) { }
    public void Sent(string notificationId, string channel) { }
    public void Failed(string notificationId, string channel, string reason) { }
    public void Retry(string notificationId, string channel, int attempt) { }
    public void SuppressedByCooldown(string alertId) { }
    public void Escalated(string alertId, int level) { }
    public void RecoverySent(string alertId) { }
    public void Grouped(int count) { }
}

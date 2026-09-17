using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Notifications;

/// <summary>A single notification delivery target (desktop toast, email, webhook, ...). Send-only.</summary>
public interface INotificationChannel
{
    string Name { get; }

    string Description { get; }

    bool RequiresCredential { get; }

    /// <summary>True when the channel is usable on this machine (e.g. desktop toast available).</summary>
    bool IsAvailable { get; }

    /// <summary>True when any required credential/configuration is present.</summary>
    bool IsConfigured { get; }

    Task SendAsync(NotificationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Decides WHETHER and WHERE to notify for a given alert event + escalation level.</summary>
public interface INotificationPolicy
{
    NotificationDecision Decide(Alert alert, NotificationEventType eventType, int escalationLevel);
}

/// <summary>Routes a notification request to its configured channels (with bounded retry) and records delivery state.</summary>
public interface INotificationService
{
    Task<IReadOnlyList<Notification>> SendAsync(NotificationRequest request, IReadOnlyList<string> channelNames,
        CancellationToken cancellationToken = default);

    IReadOnlyList<NotificationChannelMetadata> GetAvailableChannels();
}

/// <summary>Read-only notification-history queries (bounded).</summary>
public interface INotificationQuery
{
    Task<IReadOnlyList<Notification>> GetHistoryAsync(string? alertId, string? channel, NotificationStatus? status,
        DateTimeOffset? start, DateTimeOffset? end, int limit, int offset, CancellationToken cancellationToken = default);
}

/// <summary>Persists and queries notification delivery records (bounded). No secrets.</summary>
public interface INotificationStore : INotificationQuery
{
    Task RecordAsync(Notification notification, CancellationToken cancellationToken = default);

    Task<Notification?> GetAsync(string notificationId, CancellationToken cancellationToken = default);

    /// <summary>True when a SENT notification with this idempotency key already exists.</summary>
    Task<bool> HasSentIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}

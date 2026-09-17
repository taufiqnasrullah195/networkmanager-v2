namespace NETworkManager.AI.Notifications;

/// <summary>
///     Routes a notification request to its configured channels with bounded retry, and records delivery state. A
///     channel failure is logged and recorded as FAILED — it never throws into the alert/monitoring pipeline.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly IReadOnlyDictionary<string, INotificationChannel> _channels;
    private readonly INotificationStore _store;
    private readonly NotificationRetryOptions _retry;
    private readonly Func<int, Task>? _delay;

    public NotificationService(
        IReadOnlyDictionary<string, INotificationChannel> channels,
        INotificationStore store,
        NotificationRetryOptions? retry = null,
        Func<int, Task>? delay = null)
    {
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _retry = retry ?? new NotificationRetryOptions();
        _delay = delay;
    }

    public IReadOnlyList<NotificationChannelMetadata> GetAvailableChannels() =>
        _channels.Values
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => new NotificationChannelMetadata(c.Name, c.Description, c.RequiresCredential, c.IsAvailable, c.IsConfigured))
            .ToList();

    public async Task<IReadOnlyList<Notification>> SendAsync(NotificationRequest request, IReadOnlyList<string> channelNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<Notification>();

        foreach (var name in channelNames)
        {
            if (!_channels.TryGetValue(name, out var channel))
                continue;

            var notification = await SendWithRetryAsync(request, name, channel, cancellationToken).ConfigureAwait(false);
            await _store.RecordAsync(notification, cancellationToken).ConfigureAwait(false);
            results.Add(notification);
        }

        return results;
    }

    private async Task<Notification> SendWithRetryAsync(NotificationRequest request, string channelName,
        INotificationChannel channel, CancellationToken cancellationToken)
    {
        if (!channel.IsAvailable)
            return Failed(request, channelName, "channel is unavailable", 0);

        if (!channel.IsConfigured)
            return Failed(request, channelName, "channel is not configured", 0);

        Exception? last = null;

        for (var attempt = 1; attempt <= _retry.MaxAttempts; attempt++)
        {
            try
            {
                await channel.SendAsync(request, cancellationToken).ConfigureAwait(false);

                return new Notification
                {
                    NotificationId = Guid.NewGuid().ToString("N"),
                    AlertId = request.AlertId,
                    Channel = channelName,
                    Status = NotificationStatus.Sent,
                    EventType = request.EventType,
                    IdempotencyKey = request.IdempotencyKey,
                    CreatedAt = request.Timestamp,
                    SentAt = DateTimeOffset.UtcNow,
                    AttemptCount = attempt,
                };
            }
            catch (OperationCanceledException)
            {
                return new Notification
                {
                    NotificationId = Guid.NewGuid().ToString("N"),
                    AlertId = request.AlertId,
                    Channel = channelName,
                    Status = NotificationStatus.Cancelled,
                    EventType = request.EventType,
                    IdempotencyKey = request.IdempotencyKey,
                    CreatedAt = request.Timestamp,
                    AttemptCount = attempt,
                };
            }
            catch (Exception ex)
            {
                last = ex;

                if (attempt < _retry.MaxAttempts)
                    await Delay(attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        return Failed(request, channelName, SafeReason(last), _retry.MaxAttempts);
    }

    private Task Delay(int attempt, CancellationToken cancellationToken)
    {
        if (_delay is not null)
            return _delay(attempt);

        var multiplier = _retry.ExponentialBackoff ? Math.Pow(2, attempt - 1) : 1;
        return Task.Delay(TimeSpan.FromMilliseconds(_retry.BaseDelay.TotalMilliseconds * multiplier), cancellationToken);
    }

    private static Notification Failed(NotificationRequest request, string channelName, string reason, int attempts) => new()
    {
        NotificationId = Guid.NewGuid().ToString("N"),
        AlertId = request.AlertId,
        Channel = channelName,
        Status = NotificationStatus.Failed,
        EventType = request.EventType,
        IdempotencyKey = request.IdempotencyKey,
        CreatedAt = request.Timestamp,
        AttemptCount = attempts,
        FailureReason = reason,
    };

    private static string SafeReason(Exception? exception) =>
        exception is null ? "channel delivery failed" : exception.GetType().Name;
}

/// <summary>In-memory notification store (default; the SQLite store is the persistent counterpart).</summary>
public sealed class NotificationStore : INotificationStore
{
    private readonly object _gate = new();
    private readonly List<Notification> _records = new();

    public Task RecordAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        lock (_gate)
            _records.Add(notification);

        return Task.CompletedTask;
    }

    public Task<Notification?> GetAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_records.FirstOrDefault(n => n.NotificationId == notificationId));
    }

    public Task<bool> HasSentIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            return Task.FromResult(_records.Any(n => n.IdempotencyKey == idempotencyKey && n.Status == NotificationStatus.Sent));
    }

    public Task<IReadOnlyList<Notification>> GetHistoryAsync(string? alertId, string? channel, NotificationStatus? status,
        DateTimeOffset? start, DateTimeOffset? end, int limit, int offset, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IEnumerable<Notification> query = _records;

            if (!string.IsNullOrWhiteSpace(alertId))
                query = query.Where(n => n.AlertId == alertId);
            if (!string.IsNullOrWhiteSpace(channel))
                query = query.Where(n => n.Channel == channel);
            if (status is not null)
                query = query.Where(n => n.Status == status);
            if (start is not null)
                query = query.Where(n => n.CreatedAt >= start);
            if (end is not null)
                query = query.Where(n => n.CreatedAt <= end);

            var result = query.OrderByDescending(n => n.CreatedAt).Skip(offset).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<Notification>>(result);
        }
    }
}

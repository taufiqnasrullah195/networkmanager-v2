using Microsoft.Data.Sqlite;
using NETworkManager.AI.Notifications;

namespace NETworkManager.AI.Persistence;

/// <summary>SQLite-backed notification delivery history (schema v3). No secrets anywhere.</summary>
public sealed class SqliteNotificationStore : INotificationStore
{
    private readonly SqliteDatabase _db;

    public SqliteNotificationStore(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task RecordAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO notifications
                (notification_id, alert_id, channel, status, event_type, idempotency_key, created_at_ms,
                 sent_at_ms, attempt_count, failure_reason)
            VALUES
                ($id, $alertId, $channel, $status, $eventType, $idempotencyKey, $createdAt, $sentAt, $attempts, $failureReason);
            """;
        command.Parameters.AddWithValue("$id", notification.NotificationId);
        command.Parameters.AddWithValue("$alertId", notification.AlertId);
        command.Parameters.AddWithValue("$channel", notification.Channel);
        command.Parameters.AddWithValue("$status", (int)notification.Status);
        command.Parameters.AddWithValue("$eventType", (int)notification.EventType);
        command.Parameters.AddWithValue("$idempotencyKey", notification.IdempotencyKey);
        command.Parameters.AddWithValue("$createdAt", PersistenceClock.ToUtcMillis(notification.CreatedAt));
        command.Parameters.AddWithValue("$sentAt", notification.SentAt is { } sent ? PersistenceClock.ToUtcMillis(sent) : DBNull.Value);
        command.Parameters.AddWithValue("$attempts", notification.AttemptCount);
        command.Parameters.AddWithValue("$failureReason", (object?)notification.FailureReason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Notification?> GetAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM notifications WHERE notification_id = $id;";
        command.Parameters.AddWithValue("$id", notificationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<bool> HasSentIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM notifications WHERE idempotency_key = $key AND status = 1 LIMIT 1;";
        command.Parameters.AddWithValue("$key", idempotencyKey);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null;
    }

    public async Task<IReadOnlyList<Notification>> GetHistoryAsync(string? alertId, string? channel,
        NotificationStatus? status, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrWhiteSpace(alertId))
        {
            conditions.Add("alert_id = $p0");
            parameters.Add(("$p0", alertId));
        }

        if (!string.IsNullOrWhiteSpace(channel))
        {
            conditions.Add("channel = $p1");
            parameters.Add(("$p1", channel));
        }

        if (status is not null)
        {
            conditions.Add("status = $p2");
            parameters.Add(("$p2", (int)status.Value));
        }

        if (start is not null)
        {
            conditions.Add("created_at_ms >= $p3");
            parameters.Add(("$p3", PersistenceClock.ToUtcMillis(start.Value)));
        }

        if (end is not null)
        {
            conditions.Add("created_at_ms <= $p4");
            parameters.Add(("$p4", PersistenceClock.ToUtcMillis(end.Value)));
        }

        parameters.Add(("$limit", limit));
        parameters.Add(("$offset", offset));

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT * FROM notifications {(conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions))}
            ORDER BY created_at_ms DESC LIMIT $limit OFFSET $offset;
            """;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var results = new List<Notification>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(Read(reader));

        return results;
    }

    private static Notification Read(SqliteDataReader reader) => new()
    {
        NotificationId = reader.GetString(reader.GetOrdinal("notification_id")),
        AlertId = reader.GetString(reader.GetOrdinal("alert_id")),
        Channel = reader.GetString(reader.GetOrdinal("channel")),
        Status = (NotificationStatus)reader.GetInt32(reader.GetOrdinal("status")),
        EventType = (NotificationEventType)reader.GetInt32(reader.GetOrdinal("event_type")),
        IdempotencyKey = reader.GetString(reader.GetOrdinal("idempotency_key")),
        CreatedAt = PersistenceClock.FromUtcMillis(reader.GetInt64(reader.GetOrdinal("created_at_ms"))),
        SentAt = reader.IsDBNull(reader.GetOrdinal("sent_at_ms")) ? null : PersistenceClock.FromUtcMillis(reader.GetInt64(reader.GetOrdinal("sent_at_ms"))),
        AttemptCount = reader.GetInt32(reader.GetOrdinal("attempt_count")),
        FailureReason = reader.IsDBNull(reader.GetOrdinal("failure_reason")) ? null : reader.GetString(reader.GetOrdinal("failure_reason")),
    };
}

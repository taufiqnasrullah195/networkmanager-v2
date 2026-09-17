using Microsoft.Data.Sqlite;
using NETworkManager.AI.Notifications;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class NotificationPersistenceTests
{
    [Fact]
    public async Task Fresh_database_is_schema_version_3()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={db.Database.Path}");
        connection.Open();
        Assert.Equal(3L, Scalar(connection, "PRAGMA user_version;"));
        Assert.NotNull(Scalar(connection, "SELECT name FROM sqlite_master WHERE name = 'notifications';"));
    }

    [Fact]
    public async Task Store_records_and_queries_history()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        var store = new SqliteNotificationStore(db.Database);

        await store.RecordAsync(Notification("n1", "a1", "Desktop", NotificationStatus.Sent, NotificationEventType.AlertCreated, "a1:AlertCreated:0"));
        await store.RecordAsync(Notification("n2", "a2", "Email", NotificationStatus.Failed, NotificationEventType.AlertCreated, "a2:AlertCreated:0", "InvalidOperationException"));

        var all = await store.GetHistoryAsync(null, null, null, null, null, 10, 0);
        Assert.Equal(2, all.Count);

        var byAlert = await store.GetHistoryAsync("a1", null, null, null, null, 10, 0);
        Assert.Single(byAlert);
        Assert.Equal("a1", byAlert[0].AlertId);

        var sentOnly = await store.GetHistoryAsync(null, null, NotificationStatus.Sent, null, null, 10, 0);
        Assert.Single(sentOnly);
        Assert.Equal("n1", sentOnly[0].NotificationId);

        var failed = await store.GetHistoryAsync(null, null, NotificationStatus.Failed, null, null, 10, 0);
        Assert.Equal("InvalidOperationException", Assert.Single(failed).FailureReason);
    }

    [Fact]
    public async Task Has_sent_idempotency_key_is_persisted()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        var store = new SqliteNotificationStore(db.Database);

        await store.RecordAsync(Notification("n1", "a1", "Desktop", NotificationStatus.Sent, NotificationEventType.AlertCreated, "a1:AlertCreated:0"));
        await store.RecordAsync(Notification("n2", "a1", "Desktop", NotificationStatus.Failed, NotificationEventType.AlertCreated, "a1:AlertCreated:0"));

        Assert.True(await store.HasSentIdempotencyKeyAsync("a1:AlertCreated:0"));
        Assert.False(await store.HasSentIdempotencyKeyAsync("a1:AlertEscalated:1"));
    }

    private static Notification Notification(string id, string alertId, string channel, NotificationStatus status,
        NotificationEventType eventType, string idempotencyKey, string? failureReason = null) => new()
    {
        NotificationId = id,
        AlertId = alertId,
        Channel = channel,
        Status = status,
        EventType = eventType,
        IdempotencyKey = idempotencyKey,
        CreatedAt = DateTimeOffset.UtcNow,
        SentAt = status == NotificationStatus.Sent ? DateTimeOffset.UtcNow : null,
        AttemptCount = 1,
        FailureReason = failureReason,
    };

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}

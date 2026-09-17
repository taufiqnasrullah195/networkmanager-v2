using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SqliteRetentionServiceTests
{
    private static MonitoringResult OldResult() => new()
    {
        CheckId = "gw-ping",
        TargetId = "gw",
        CheckType = MonitorCheckType.Ping,
        Status = MonitoringResultStatus.Healthy,
        Timestamp = DateTimeOffset.UtcNow.AddDays(-31),
        Duration = TimeSpan.Zero,
        SafeMessage = "old",
    };

    private static Alert OldResolvedAlert() => new()
    {
        AlertId = "old-resolved",
        Fingerprint = "gw\u0001Ping",
        TargetId = "gw",
        TargetName = "Gateway",
        Severity = AlertSeverity.Error,
        Status = AlertStatus.Resolved,
        Title = "old",
        FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-200),
        LastSeenAt = DateTimeOffset.UtcNow.AddDays(-181),
        ResolvedAt = DateTimeOffset.UtcNow.AddDays(-181),
        PreviousHealthState = NetworkHealthStatus.Healthy,
        CurrentHealthState = NetworkHealthStatus.Unhealthy,
    };

    private static Alert ActiveAlert() => new()
    {
        AlertId = "active",
        Fingerprint = "gw\u0001Ping",
        TargetId = "gw",
        TargetName = "Gateway",
        Severity = AlertSeverity.Error,
        Status = AlertStatus.Open,
        Title = "active",
        FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-200),
        LastSeenAt = DateTimeOffset.UtcNow,
        PreviousHealthState = NetworkHealthStatus.Healthy,
        CurrentHealthState = NetworkHealthStatus.Unhealthy,
    };

    [Fact]
    public async Task Expired_records_deleted_recent_preserved()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        store.Record(OldResult());
        store.Record(new MonitoringResult
        {
            CheckId = "gw-ping", TargetId = "gw", CheckType = MonitorCheckType.Ping,
            Status = MonitoringResultStatus.Healthy, Timestamp = DateTimeOffset.UtcNow,
            Duration = TimeSpan.Zero, SafeMessage = "recent",
        });

        var retention = new SqliteRetentionService(db.Database);
        await retention.RunCleanupAsync(new RetentionPolicy());

        var history = new SqliteHistoryRepository(db.Database);
        var results = await history.GetResultsAsync("gw", null, null, 10, 0);

        Assert.Single(results);
        Assert.Equal("recent", results[0].SafeMessage);
    }

    [Fact]
    public async Task Active_alerts_preserved_resolved_deleted()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteAlertStore(db.Database);
        store.CreateAlert(OldResolvedAlert());
        store.CreateAlert(ActiveAlert());

        var retention = new SqliteRetentionService(db.Database);
        await retention.RunCleanupAsync(new RetentionPolicy());

        var restarted = new SqliteAlertStore(db.Database);
        await restarted.LoadActiveAlertsAsync();

        // Only the active alert survives; the old resolved one is gone.
        var active = Assert.Single(restarted.GetActiveAlerts());
        Assert.Equal("active", active.AlertId);

        var history = new SqliteHistoryRepository(db.Database);
        var all = await history.GetAlertHistoryAsync(null, null, null, 10, 0);
        Assert.DoesNotContain(all, a => a.AlertId == "old-resolved");
    }

    [Fact]
    public async Task Expired_transitions_are_deleted()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        store.SetHealth("gw", NetworkHealthStatus.Unhealthy, DateTimeOffset.UtcNow.AddDays(-91));
        store.SetHealth("gw", NetworkHealthStatus.Healthy, DateTimeOffset.UtcNow);

        var retention = new SqliteRetentionService(db.Database);
        await retention.RunCleanupAsync(new RetentionPolicy());

        var history = new SqliteHistoryRepository(db.Database);
        var transitions = await history.GetTransitionsAsync("gw", null, null, 10, 0);

        Assert.Single(transitions); // only the recent transition survives
    }
}
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SqliteAlertStoreTests
{
    private static Alert Alert(string id = "a1", AlertStatus status = AlertStatus.Open, int occurrences = 1) => new()
    {
        AlertId = id,
        Fingerprint = "gw\u0001Ping",
        TargetId = "gw",
        TargetName = "Gateway",
        Severity = AlertSeverity.Error,
        Status = status,
        Title = "Gateway became unhealthy.",
        Reason = "unreachable",
        Evidence = "4 probes, 0 successful, 0 timed out",
        FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        LastSeenAt = DateTimeOffset.UtcNow,
        OccurrenceCount = occurrences,
        PreviousHealthState = NetworkHealthStatus.Healthy,
        CurrentHealthState = NetworkHealthStatus.Unhealthy,
        FailureClassification = MonitorErrorClass.Unreachable.ToString(),
    };

    [Fact]
    public async Task Alert_persists_and_survives_restart()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteAlertStore(db.Database);
        store.CreateAlert(Alert());

        // "Restart": a new store over the same database restores the active alert.
        var restarted = new SqliteAlertStore(db.Database);
        await restarted.LoadActiveAlertsAsync();

        var restored = Assert.Single(restarted.GetActiveAlerts());
        Assert.Equal("a1", restored.AlertId);
        Assert.Equal(AlertSeverity.Error, restored.Severity);
    }

    [Fact]
    public async Task Acknowledgement_and_resolution_persist()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteAlertStore(db.Database);
        store.CreateAlert(Alert());

        store.UpdateAlert(Alert("a1", AlertStatus.Acknowledged) with { AcknowledgedAt = DateTimeOffset.UtcNow });
        store.UpdateAlert(Alert("a1", AlertStatus.Resolved) with { ResolvedAt = DateTimeOffset.UtcNow, ResolutionEvidence = "recovered" });

        var restarted = new SqliteAlertStore(db.Database);
        await restarted.LoadActiveAlertsAsync();

        // Resolved → not active after restart.
        Assert.Empty(restarted.GetActiveAlerts());

        var history = new SqliteHistoryRepository(db.Database);
        var alerts = await history.GetAlertHistoryAsync("gw", null, null, 10, 0);
        var entry = Assert.Single(alerts);
        Assert.Equal(AlertStatus.Resolved, entry.Status);
        Assert.NotNull(entry.ResolvedAt);
        Assert.Equal("recovered", entry.ResolutionEvidence);
    }

    [Fact]
    public async Task Occurrences_are_persisted_separately()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteAlertStore(db.Database);
        store.CreateAlert(Alert(occurrences: 1));

        store.UpdateAlert(Alert("a1", occurrences: 2) with { LastSeenAt = DateTimeOffset.UtcNow.AddSeconds(30) });
        store.UpdateAlert(Alert("a1", occurrences: 3) with { LastSeenAt = DateTimeOffset.UtcNow.AddSeconds(60) });

        var history = new SqliteHistoryRepository(db.Database);
        var occurrences = await history.GetOccurrencesAsync("a1", 10, 0);

        Assert.Equal(2, occurrences.Count); // two increments → two occurrence rows
    }

    [Fact]
    public async Task Alert_history_is_queryable_with_pagination()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteAlertStore(db.Database);
        store.CreateAlert(Alert("a1"));
        store.CreateAlert(Alert("a2", AlertStatus.Resolved));

        var history = new SqliteHistoryRepository(db.Database);
        var all = await history.GetAlertHistoryAsync(null, null, null, 10, 0);
        Assert.Equal(2, all.Count);

        var one = await history.GetAlertHistoryAsync(null, null, null, 1, 1);
        Assert.Single(one);
    }

    [Fact]
    public void Unavailable_database_degrades_to_in_memory()
    {
        var store = new SqliteAlertStore(new SqliteDatabase("/nonexistent/deep/x.db"));
        store.CreateAlert(Alert());
        Assert.Single(store.GetActiveAlerts());
        Assert.True(store.PersistenceFaulted);
    }
}
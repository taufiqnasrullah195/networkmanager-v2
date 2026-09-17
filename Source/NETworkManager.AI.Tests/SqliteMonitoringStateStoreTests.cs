using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SqliteMonitoringStateStoreTests
{
    private static MonitoringResult Result(string targetId = "gw", MonitoringResultStatus status = MonitoringResultStatus.Healthy, DateTimeOffset? timestamp = null) => new()
    {
        CheckId = $"{targetId}-ping",
        TargetId = targetId,
        CheckType = MonitorCheckType.Ping,
        Status = status,
        ErrorClassification = status == MonitoringResultStatus.Unhealthy ? MonitorErrorClass.Unreachable : MonitorErrorClass.None,
        Timestamp = timestamp ?? DateTimeOffset.UtcNow,
        Duration = TimeSpan.FromMilliseconds(5),
        SafeMessage = status == MonitoringResultStatus.Healthy ? "ok" : "unreachable",
        CorrelationId = $"{targetId}-ping",
    };

    [Fact]
    public async Task Result_persists_and_is_queryable()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        var history = new SqliteHistoryRepository(db.Database);

        store.Record(Result("gw"));

        var results = await history.GetResultsAsync("gw", null, null, 10, 0);
        var entry = Assert.Single(results);
        Assert.Equal("gw", entry.TargetId);
        Assert.Equal(MonitoringResultStatus.Healthy, entry.Status);
        Assert.Equal(MonitorCheckType.Ping, entry.CheckType);
    }

    [Fact]
    public async Task Latest_result_is_retrievable_from_runtime_store()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        store.Record(Result("gw"));
        store.Record(Result("gw", MonitoringResultStatus.Unhealthy)); // replaces

        var latest = store.GetLatestResults("gw");
        Assert.Equal(MonitoringResultStatus.Unhealthy, Assert.Single(latest).Status);
    }

    [Fact]
    public async Task Transition_persists_only_on_actual_change()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        var history = new SqliteHistoryRepository(db.Database);

        store.SetHealth("gw", NetworkHealthStatus.Unhealthy, DateTimeOffset.UtcNow);
        store.SetHealth("gw", NetworkHealthStatus.Unhealthy, DateTimeOffset.UtcNow); // no change
        store.SetHealth("gw", NetworkHealthStatus.Healthy, DateTimeOffset.UtcNow);    // change

        var transitions = await history.GetTransitionsAsync("gw", null, null, 10, 0);
        Assert.Equal(2, transitions.Count);
        Assert.Equal(NetworkHealthStatus.Healthy, transitions[0].NewState); // most recent first
    }

    [Fact]
    public async Task Pagination_returns_bounded_results()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        var history = new SqliteHistoryRepository(db.Database);

        for (var i = 0; i < 10; i++)
            store.Record(Result("gw", MonitoringResultStatus.Healthy, DateTimeOffset.UtcNow.AddSeconds(-i)));

        var page1 = await history.GetResultsAsync("gw", null, null, 4, 0);
        var page2 = await history.GetResultsAsync("gw", null, null, 4, 4);

        Assert.Equal(4, page1.Count);
        Assert.Equal(4, page2.Count);
        Assert.NotEqual(page1[0].Id, page2[0].Id);
    }

    [Fact]
    public async Task Concurrent_writes_do_not_corrupt_state()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);

        Parallel.For(0, 50, i =>
            store.Record(Result("gw", MonitoringResultStatus.Healthy, DateTimeOffset.UtcNow.AddMilliseconds(i))));

        var history = new SqliteHistoryRepository(db.Database);
        var results = await history.GetResultsAsync("gw", null, null, 200, 0);
        Assert.Equal(50, results.Count);
    }

    [Fact]
    public void Unavailable_database_degrades_to_in_memory()
    {
        var store = new SqliteMonitoringStateStore(new SqliteDatabase("/nonexistent/deep/x.db"));

        store.Record(Result("gw"));

        Assert.Single(store.GetLatestResults("gw"));
        Assert.True(store.PersistenceFaulted);
    }

    [Fact]
    public async Task Fresh_store_does_not_treat_persisted_health_as_current()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        store.SetHealth("gw", NetworkHealthStatus.Unhealthy, DateTimeOffset.UtcNow);

        // A fresh store over the same DB starts with no current state — historical data is not replayed as current.
        var fresh = new SqliteMonitoringStateStore(db.Database);
        Assert.Equal(NetworkHealthStatus.Unknown, fresh.GetHealth("gw"));
        Assert.Empty(fresh.GetLatestResults("gw"));
    }
}
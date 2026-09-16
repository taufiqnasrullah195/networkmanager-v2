using NETworkManager.AI.Monitoring;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringStateStoreTests
{
    private static MonitoringResult Result(string targetId, MonitorCheckType type, MonitoringResultStatus status) => new()
    {
        CheckId = $"{targetId}-{type}",
        TargetId = targetId,
        CheckType = type,
        Status = status,
        Timestamp = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        SafeMessage = "m",
    };

    [Fact]
    public void Records_latest_per_target_and_check_type()
    {
        var store = new MonitoringStateStore();

        store.Record(Result("t1", MonitorCheckType.Ping, MonitoringResultStatus.Healthy));
        store.Record(Result("t1", MonitorCheckType.Ping, MonitoringResultStatus.Unhealthy)); // replaces
        store.Record(Result("t1", MonitorCheckType.DnsResolution, MonitoringResultStatus.Healthy));

        var latest = store.GetLatestResults("t1");
        Assert.Equal(2, latest.Count);
        Assert.Equal(MonitoringResultStatus.Unhealthy, latest.Single(r => r.CheckType == MonitorCheckType.Ping).Status);
    }

    [Fact]
    public void Health_defaults_to_unknown_and_round_trips()
    {
        var store = new MonitoringStateStore();

        Assert.Equal(NetworkHealthStatus.Unknown, store.GetHealth("t1"));

        store.SetHealth("t1", NetworkHealthStatus.Degraded, DateTimeOffset.UtcNow);
        Assert.Equal(NetworkHealthStatus.Degraded, store.GetHealth("t1"));
    }

    [Fact]
    public void Recent_failures_exclude_healthy_and_are_most_recent_first()
    {
        var store = new MonitoringStateStore();

        store.Record(Result("t1", MonitorCheckType.Ping, MonitoringResultStatus.Healthy));
        store.Record(Result("t1", MonitorCheckType.TcpConnectivity, MonitoringResultStatus.Timeout));
        store.Record(Result("t1", MonitorCheckType.DnsResolution, MonitoringResultStatus.Unhealthy));
        store.Record(Result("t1", MonitorCheckType.Ping, MonitoringResultStatus.Warning)); // warning not a failure

        var failures = store.GetRecentFailures(10);
        Assert.Equal(2, failures.Count); // timeout + unhealthy only
        Assert.Equal(MonitorCheckType.DnsResolution, failures[0].CheckType); // most recent first
    }

    [Fact]
    public void Remove_target_clears_state()
    {
        var store = new MonitoringStateStore();
        store.Record(Result("t1", MonitorCheckType.Ping, MonitoringResultStatus.Unhealthy));
        store.SetHealth("t1", NetworkHealthStatus.Unhealthy, DateTimeOffset.UtcNow);

        store.RemoveTarget("t1");

        Assert.Empty(store.GetLatestResults("t1"));
        Assert.Equal(NetworkHealthStatus.Unknown, store.GetHealth("t1"));
        Assert.Empty(store.GetRecentFailures(10));
    }
}
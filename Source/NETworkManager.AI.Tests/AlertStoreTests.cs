using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using Xunit;

namespace NETworkManager.AI.Tests;

public class AlertStoreTests
{
    private static Alert Alert(string id, AlertStatus status, AlertSeverity severity, string targetId = "gw") => new()
    {
        AlertId = id,
        Fingerprint = $"{targetId}\u0001Ping",
        TargetId = targetId,
        TargetName = targetId,
        Severity = severity,
        Status = status,
        Title = "test",
        FirstSeenAt = DateTimeOffset.UtcNow,
        LastSeenAt = DateTimeOffset.UtcNow,
        PreviousHealthState = NetworkHealthStatus.Healthy,
        CurrentHealthState = NetworkHealthStatus.Unhealthy,
    };

    [Fact]
    public void Create_and_get_round_trip()
    {
        var store = new AlertStore();
        var created = store.CreateAlert(Alert("a", AlertStatus.Open, AlertSeverity.Error));

        Assert.Same(created, store.GetAlert("a"));
    }

    [Fact]
    public void Update_replaces_by_id()
    {
        var store = new AlertStore();
        store.CreateAlert(Alert("a", AlertStatus.Open, AlertSeverity.Error));

        var updated = Alert("a", AlertStatus.Acknowledged, AlertSeverity.Error);
        Assert.True(store.UpdateAlert(updated));
        Assert.Equal(AlertStatus.Acknowledged, store.GetAlert("a")!.Status);

        Assert.False(store.UpdateAlert(Alert("missing", AlertStatus.Open, AlertSeverity.Error)));
    }

    [Fact]
    public void Active_excludes_resolved_and_suppressed()
    {
        var store = new AlertStore();
        store.CreateAlert(Alert("a", AlertStatus.Open, AlertSeverity.Error));
        store.CreateAlert(Alert("b", AlertStatus.Acknowledged, AlertSeverity.Warning));
        store.CreateAlert(Alert("c", AlertStatus.Resolved, AlertSeverity.Error));

        var active = store.GetActiveAlerts();
        Assert.Equal(2, active.Count);
        Assert.DoesNotContain(active, a => a.AlertId == "c");
    }

    [Fact]
    public void Recent_returns_most_recent_first_including_resolved()
    {
        var store = new AlertStore();
        store.CreateAlert(Alert("a", AlertStatus.Open, AlertSeverity.Error));
        store.CreateAlert(Alert("b", AlertStatus.Resolved, AlertSeverity.Error));

        var recent = store.GetRecentAlerts(10);
        Assert.Equal(2, recent.Count);
        Assert.Equal("b", recent[0].AlertId); // most recently created is first
    }
}
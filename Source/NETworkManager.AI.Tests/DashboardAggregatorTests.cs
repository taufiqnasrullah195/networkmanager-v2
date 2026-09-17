using NETworkManager.AI.Alerts;
using NETworkManager.AI.Dashboard;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Snmp;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class DashboardAggregatorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static DashboardAggregator Build(
        FakeMonitoringQuery? monitoring = null,
        FakeAlertQuery? alerts = null,
        FakeMonitoringHistoryRepository? history = null,
        FakeAlertHistoryRepository? alertHistory = null,
        FakeSnmpTelemetryRepository? snmp = null,
        IReadOnlyDictionary<string, MonitoringTarget>? targets = null,
        bool running = true) =>
        new(monitoring ?? new FakeMonitoringQuery(), alerts ?? new FakeAlertQuery(), history, alertHistory, snmp,
            targets, () => running);

    [Fact]
    public async Task Build_loads_and_computes_health_counts()
    {
        var monitoring = new FakeMonitoringQuery
        {
            Statuses = new[]
            {
                DashboardTestData.Snapshot("sw01", NetworkHealthStatus.Healthy, Now.AddSeconds(-5)),
                DashboardTestData.Snapshot("sw02", NetworkHealthStatus.Degraded, Now.AddSeconds(-5)),
                DashboardTestData.Snapshot("srv01", NetworkHealthStatus.Unhealthy, Now.AddSeconds(-5)),
            },
        };

        var snapshot = await Build(monitoring, running: true).BuildAsync();

        Assert.Equal(1, snapshot.Health.Healthy);
        Assert.Equal(1, snapshot.Health.Degraded);
        Assert.Equal(1, snapshot.Health.Unhealthy);
        Assert.Equal(3, snapshot.Health.MonitoredDevices);
        Assert.True(snapshot.Health.MonitoringRunning);
        Assert.NotEqual(default, snapshot.GeneratedAt);
    }

    [Fact]
    public async Task Monitoring_stopped_is_reflected()
    {
        var monitoring = new FakeMonitoringQuery { Statuses = new[] { DashboardTestData.Snapshot("sw01", NetworkHealthStatus.Healthy) } };

        var snapshot = await Build(monitoring, running: false).BuildAsync();

        Assert.False(snapshot.Health.MonitoringRunning);
    }

    [Fact]
    public async Task Stale_devices_are_detected_and_counted_separately()
    {
        var monitoring = new FakeMonitoringQuery
        {
            Statuses = new[]
            {
                DashboardTestData.Snapshot("fresh", NetworkHealthStatus.Healthy, Now.AddSeconds(-5)),
                DashboardTestData.Snapshot("stale", NetworkHealthStatus.Healthy, Now.AddMinutes(-10)),
            },
        };

        var snapshot = await Build(monitoring).BuildAsync();

        Assert.Equal(1, snapshot.Health.Healthy);
        Assert.Equal(1, snapshot.Health.Stale);
        var staleRow = Assert.Single(snapshot.Devices, d => d.DeviceId == "stale");
        Assert.True(staleRow.IsStale);
        Assert.Equal("Stale", staleRow.Health);
    }

    [Fact]
    public async Task Alert_counts_are_derived_from_active_alerts()
    {
        var alerts = new FakeAlertQuery
        {
            Active = new[]
            {
                DashboardTestData.ActiveAlert("a1", "srv01", AlertSeverity.Error),
                DashboardTestData.ActiveAlert("a2", "sw02", AlertSeverity.Warning),
            },
        };

        var snapshot = await Build(alerts: alerts).BuildAsync();

        Assert.Equal(2, snapshot.Health.ActiveAlerts);
        Assert.Equal(2, snapshot.ActiveAlerts.Count);
    }

    [Fact]
    public async Task Device_status_filter_works()
    {
        var monitoring = new FakeMonitoringQuery
        {
            Statuses = new[]
            {
                DashboardTestData.Snapshot("sw01", NetworkHealthStatus.Healthy),
                DashboardTestData.Snapshot("srv01", NetworkHealthStatus.Unhealthy),
            },
        };

        var snapshot = await Build(monitoring).BuildAsync(statusFilter: "Unhealthy");

        var device = Assert.Single(snapshot.Devices);
        Assert.Equal("srv01", device.DeviceId);
    }

    [Fact]
    public async Task Device_search_matches_name_address_and_id()
    {
        var monitoring = new FakeMonitoringQuery
        {
            Statuses = new[]
            {
                DashboardTestData.Snapshot("core-switch", NetworkHealthStatus.Healthy),
                DashboardTestData.Snapshot("edge-router", NetworkHealthStatus.Healthy),
            },
        };
        var targets = new Dictionary<string, MonitoringTarget>
        {
            ["core-switch"] = new() { Id = "core-switch", IPAddress = "10.0.0.10" },
            ["edge-router"] = new() { Id = "edge-router", IPAddress = "192.168.1.1" },
        };

        var byAddress = await Build(monitoring, targets: targets).BuildAsync(search: "192.168.1.1");
        Assert.Equal("edge-router", Assert.Single(byAddress.Devices).DeviceId);

        var byId = await Build(monitoring, targets: targets).BuildAsync(search: "core");
        Assert.Equal("core-switch", Assert.Single(byId.Devices).DeviceId);
    }

    [Fact]
    public async Task Device_rows_include_type_address_and_snmp_flag()
    {
        var monitoring = new FakeMonitoringQuery
        {
            Statuses = new[] { DashboardTestData.Snapshot("sw01", NetworkHealthStatus.Healthy) },
        };
        var targets = new Dictionary<string, MonitoringTarget>
        {
            ["sw01"] = new() { Id = "sw01", Type = MonitorTargetType.Switch, IPAddress = "10.0.0.10" },
        };
        var snmp = new FakeSnmpTelemetryRepository
        {
            LatestDevice = new DeviceTelemetry { DeviceId = "sw01", Target = "10.0.0.10", Timestamp = Now, Reachable = true },
        };

        var snapshot = await Build(monitoring, snmp: snmp, targets: targets).BuildAsync();

        var row = Assert.Single(snapshot.Devices);
        Assert.Equal("Switch", row.Type);
        Assert.Equal("10.0.0.10", row.Address);
        Assert.True(row.SnmpAvailable);
    }

    [Fact]
    public async Task Interface_health_distinguishes_admin_down_from_operational_down()
    {
        var snmp = new FakeSnmpTelemetryRepository
        {
            AllLatestInterfaces = new[]
            {
                Iface("sw01", 1, SnmpInterfaceAdminStatus.Up, SnmpInterfaceOperStatus.Up),
                Iface("sw01", 2, SnmpInterfaceAdminStatus.Up, SnmpInterfaceOperStatus.Down),   // operational down
                Iface("sw01", 3, SnmpInterfaceAdminStatus.Down, SnmpInterfaceOperStatus.Down),  // admin down
            },
        };

        var snapshot = await Build(snmp: snmp).BuildAsync();

        Assert.Equal(1, snapshot.Interfaces.Up);
        Assert.Equal(1, snapshot.Interfaces.Down);
        Assert.Equal(1, snapshot.Interfaces.AdminDown);
        Assert.Equal(3, snapshot.Interfaces.Total);

        var issue = Assert.Single(snapshot.InterfaceIssues, i => i.Kind == InterfaceIssueKind.OperationalDown);
        Assert.Equal(2, issue.InterfaceIndex);
    }

    [Fact]
    public async Task Bandwidth_is_computed_from_two_samples()
    {
        var previous = Iface("sw01", 1, SnmpInterfaceAdminStatus.Up, SnmpInterfaceOperStatus.Up, inOctets: 0, outOctets: 0);
        var current = Iface("sw01", 1, SnmpInterfaceAdminStatus.Up, SnmpInterfaceOperStatus.Up, inOctets: 100_000_000, outOctets: 50_000_000);
        previous = previous with { Timestamp = Now.AddSeconds(-1) };
        current = current with { Timestamp = Now, SpeedBitsPerSecond = 1_000_000_000 };

        var snmp = new FakeSnmpTelemetryRepository { InterfaceHistory = new[] { current, previous } };

        var bandwidth = await Build(snmp: snmp).GetBandwidthAsync("sw01", 1);

        Assert.NotNull(bandwidth);
        // 100_000_000 octets * 8 = 800 Mbit over 1s = 800 Mbps.
        Assert.Equal(800.0, bandwidth!.RxMbps!.Value, 1);
        Assert.Equal(400.0, bandwidth.TxMbps!.Value, 1);
        Assert.Equal(80.0, bandwidth.UtilizationPercent!.Value, 1);
    }

    [Fact]
    public async Task Bandwidth_insufficient_samples_returns_null()
    {
        var snmp = new FakeSnmpTelemetryRepository { InterfaceHistory = new[] { Iface("sw01", 1, SnmpInterfaceAdminStatus.Up, SnmpInterfaceOperStatus.Up) } };

        var bandwidth = await Build(snmp: snmp).GetBandwidthAsync("sw01", 1);

        Assert.Null(bandwidth);
    }

    [Fact]
    public async Task Latency_is_derived_from_current_ping_telemetry()
    {
        var monitoring = new FakeMonitoringQuery
        {
            Statuses = new[]
            {
                DashboardTestData.Snapshot("gw", NetworkHealthStatus.Healthy, Now,
                    DashboardTestData.PingCheck("gw", 4.2, 0.4)),
            },
        };

        var snapshot = await Build(monitoring).BuildAsync();

        var latency = Assert.Single(snapshot.Latency);
        Assert.Equal("gw", latency.TargetId);
        Assert.Equal(4.2, latency.AverageLatencyMs!.Value, 3);
        Assert.Equal(0.4, latency.PacketLossPercent!.Value, 3);
    }

    [Fact]
    public async Task Availability_is_computed_from_history()
    {
        var monitoring = new FakeMonitoringQuery { Statuses = new[] { DashboardTestData.Snapshot("gw", NetworkHealthStatus.Healthy) } };
        var history = new FakeMonitoringHistoryRepository
        {
            Results = new[]
            {
                DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Healthy, Now.AddMinutes(-1)),
                DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Timeout, Now.AddMinutes(-2)),
                DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Healthy, Now.AddMinutes(-3)),
                DashboardTestData.ResultEntry("gw", MonitoringResultStatus.Healthy, Now.AddMinutes(-4)),
            },
        };

        var availability = await Build(monitoring, history: history).GetAvailabilityAsync(TimeSpan.FromHours(24));

        var item = Assert.Single(availability);
        Assert.Equal(75.0, item.AvailabilityPercent!.Value, 1);
        Assert.Equal(3, item.HealthyObservations);
        Assert.Equal(4, item.TotalObservations);
    }

    [Fact]
    public async Task Health_trend_builds_a_state_timeline()
    {
        var monitoring = new FakeMonitoringQuery { Statuses = new[] { DashboardTestData.Snapshot("gw", NetworkHealthStatus.Healthy, Now) } };
        var history = new FakeMonitoringHistoryRepository
        {
            Transitions = new[]
            {
                DashboardTestData.Transition("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy, Now.AddMinutes(-5)),
                DashboardTestData.Transition("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy, Now.AddMinutes(-1)),
            },
        };

        var points = await Build(monitoring, history: history).GetHealthTrendAsync("gw", TimeSpan.FromHours(24));

        Assert.Equal(TrendHealth.Unhealthy, points[0].Health);
        Assert.Equal(TrendHealth.Healthy, points[1].Health);
        Assert.Equal(TrendHealth.Healthy, points[^1].Health); // current state appended
    }

    [Fact]
    public async Task Alert_trend_counts_created_resolved_active()
    {
        var alerts = new FakeAlertQuery { Active = new[] { DashboardTestData.ActiveAlert("a1", "srv01") } };
        var alertHistory = new FakeAlertHistoryRepository
        {
            Alerts = new[]
            {
                DashboardTestData.AlertEntry("a1", "srv01", Now.AddMinutes(-30)),
                DashboardTestData.AlertEntry("a2", "srv01", Now.AddMinutes(-20), Now.AddMinutes(-10)),
            },
        };

        var trend = await Build(alerts: alerts, alertHistory: alertHistory).GetAlertTrendAsync(TimeSpan.FromHours(24));

        Assert.Equal(2, trend.Created);
        Assert.Equal(1, trend.Resolved);
        Assert.Equal(1, trend.Active);
    }

    [Fact]
    public async Task Recent_events_include_health_transitions_and_alert_creation()
    {
        var monitoring = new FakeMonitoringQuery { Statuses = new[] { DashboardTestData.Snapshot("gw", NetworkHealthStatus.Healthy) } };
        var alerts = new FakeAlertQuery { Active = new[] { DashboardTestData.ActiveAlert("a1", "srv01", firstSeen: Now.AddMinutes(-2)) } };
        var history = new FakeMonitoringHistoryRepository
        {
            Transitions = new[] { DashboardTestData.Transition("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy, Now.AddMinutes(-5)) },
        };

        var snapshot = await Build(monitoring, alerts: alerts, history: history).BuildAsync();

        Assert.Contains(snapshot.RecentEvents, e => e.Kind == RecentEventKind.HealthChanged);
        Assert.Contains(snapshot.RecentEvents, e => e.Kind == RecentEventKind.AlertCreated);
    }

    private static InterfaceTelemetry Iface(string deviceId, int index, SnmpInterfaceAdminStatus admin,
        SnmpInterfaceOperStatus oper, ulong? inOctets = 1000, ulong? outOctets = 2000, ulong? inErrors = 0,
        ulong? inDiscards = 0) => new()
    {
        DeviceId = deviceId,
        InterfaceIndex = index,
        Name = $"Gi1/0/{index}",
        AdminStatus = admin,
        OperationalStatus = oper,
        InOctets = inOctets,
        OutOctets = outOctets,
        InErrors = inErrors,
        OutErrors = 0,
        InDiscards = inDiscards,
        OutDiscards = 0,
        Timestamp = Now,
    };
}

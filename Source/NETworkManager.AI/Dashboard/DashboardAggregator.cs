using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Snmp;

namespace NETworkManager.AI.Dashboard;

/// <summary>
///     Aggregates existing monitoring/alert/history/SNMP services into a single dashboard snapshot. Pure presentation
///     aggregation — it owns no monitoring, alert, SNMP, or AI logic, performs no network calls, and never fabricates
///     a statistic: missing data becomes null/"unavailable"/"stale", not a fake healthy state.
/// </summary>
public sealed class DashboardAggregator
{
    private readonly IMonitoringQuery _monitoring;
    private readonly IAlertQuery _alerts;
    private readonly IMonitoringHistoryRepository? _history;
    private readonly IAlertHistoryRepository? _alertHistory;
    private readonly ISnmpTelemetryRepository? _snmp;
    private readonly IReadOnlyDictionary<string, MonitoringTarget>? _targets;
    private readonly Func<bool>? _monitoringRunning;
    private readonly DashboardOptions _options;
    private readonly InterfaceRateCalculator _rateCalculator = new();

    public DashboardAggregator(
        IMonitoringQuery monitoring,
        IAlertQuery alerts,
        IMonitoringHistoryRepository? history = null,
        IAlertHistoryRepository? alertHistory = null,
        ISnmpTelemetryRepository? snmp = null,
        IReadOnlyDictionary<string, MonitoringTarget>? targets = null,
        Func<bool>? monitoringRunning = null,
        DashboardOptions? options = null)
    {
        _monitoring = monitoring ?? throw new ArgumentNullException(nameof(monitoring));
        _alerts = alerts ?? throw new ArgumentNullException(nameof(alerts));
        _history = history;
        _alertHistory = alertHistory;
        _snmp = snmp;
        _targets = targets;
        _monitoringRunning = monitoringRunning;
        _options = options ?? new DashboardOptions();
    }

    /// <summary>Builds the current-state snapshot (health, devices, alerts, events, interfaces, latency).</summary>
    public async Task<DashboardSnapshot> BuildAsync(string? statusFilter = null, string? search = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var statuses = _monitoring.GetCurrentStatus().Take(_options.MaxDevices).ToList();

        var alertCounts = _alerts.GetActiveAlerts()
            .GroupBy(a => a.TargetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var health = ComputeHealthSummary(statuses, now);
        var devices = await BuildDeviceRowsAsync(statuses, alertCounts, now, cancellationToken).ConfigureAwait(false);
        devices = ApplyDeviceFilter(devices, statusFilter, search);

        var activeAlerts = _alerts.GetActiveAlerts().Take(_options.MaxActiveAlerts).Select(ToAlertCard).ToList();
        var recentEvents = await BuildRecentEventsAsync(now, cancellationToken).ConfigureAwait(false);
        var (interfaces, issues) = await BuildInterfaceDataAsync(cancellationToken).ConfigureAwait(false);
        var latency = BuildLatency(statuses);

        return new DashboardSnapshot(health, devices, activeAlerts, recentEvents, interfaces, issues, latency, now);
    }

    /// <summary>Per-target availability over a window. Availability = healthy / total valid (non-UNKNOWN) observations.</summary>
    public async Task<IReadOnlyList<AvailabilityItem>> GetAvailabilityAsync(TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (_history is null)
            return Array.Empty<AvailabilityItem>();

        var now = DateTimeOffset.UtcNow;
        var start = now - window;
        var entries = await _history.GetResultsAsync(null, start, now, _options.HistoryQueryLimit, 0, cancellationToken)
            .ConfigureAwait(false);

        var names = _monitoring.GetCurrentStatus()
            .ToDictionary(s => s.TargetId, s => s.DisplayName, StringComparer.OrdinalIgnoreCase);

        var result = new List<AvailabilityItem>();
        foreach (var group in entries.GroupBy(e => e.TargetId, StringComparer.OrdinalIgnoreCase))
        {
            var (percent, healthy, total) = DashboardCalculators.ComputeAvailability(group, start);
            names.TryGetValue(group.Key, out var name);
            result.Add(new AvailabilityItem(group.Key, name ?? group.Key, percent, healthy, total, Complete: true));
        }

        return result.OrderByDescending(i => i.AvailabilityPercent ?? -1).ToList();
    }

    /// <summary>State timeline for one target from persisted transitions + the current state (a step function).</summary>
    public async Task<IReadOnlyList<HealthTrendPoint>> GetHealthTrendAsync(string targetId, TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        if (_history is null)
            return Array.Empty<HealthTrendPoint>();

        var now = DateTimeOffset.UtcNow;
        var start = now - window;
        var transitions = await _history.GetTransitionsAsync(targetId, start, now, _options.HistoryQueryLimit, 0, cancellationToken)
            .ConfigureAwait(false);

        var points = transitions
            .OrderBy(t => t.Timestamp)
            .Select(t => new HealthTrendPoint(t.Timestamp, DashboardCalculators.ToTrendHealth(t.NewState)))
            .ToList();

        var current = _monitoring.GetHealth(targetId);
        if (current is not null)
            points.Add(new HealthTrendPoint(now, DashboardCalculators.ToTrendHealth(current.Value)));

        return points;
    }

    /// <summary>Created/resolved/active alert counts over a window from persisted alert history.</summary>
    public async Task<AlertTrend> GetAlertTrendAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var start = now - window;

        if (_alertHistory is null)
            return new AlertTrend(0, 0, _alerts.GetActiveAlerts().Count, start, now);

        var alerts = await _alertHistory.GetAlertHistoryAsync(null, start, now, _options.HistoryQueryLimit, 0, cancellationToken)
            .ConfigureAwait(false);

        var created = alerts.Count(a => a.FirstSeenAt >= start);
        var resolved = alerts.Count(a => a.ResolvedAt is not null && a.ResolvedAt >= start);
        var active = _alerts.GetActiveAlerts().Count;

        return new AlertTrend(created, resolved, active, start, now);
    }

    /// <summary>Bandwidth (rate) for one interface from its last two samples, or null when insufficient/unsafe.</summary>
    public async Task<BandwidthItem?> GetBandwidthAsync(string deviceId, int interfaceIndex,
        CancellationToken cancellationToken = default)
    {
        if (_snmp is null)
            return null;

        var history = await _snmp.GetInterfaceTelemetryHistoryAsync(deviceId, interfaceIndex, null, null, 2, 0, cancellationToken)
            .ConfigureAwait(false);

        if (history.Count < 2)
            return null;

        var current = history[0];
        var previous = history[1];

        if (current.InOctets is null || current.OutOctets is null || previous.InOctets is null || previous.OutOctets is null)
            return null;

        var rate = _rateCalculator.Calculate(
            new InterfaceRateSample { InOctets = previous.InOctets.Value, OutOctets = previous.OutOctets.Value, Timestamp = previous.Timestamp, SpeedBitsPerSecond = current.SpeedBitsPerSecond },
            new InterfaceRateSample { InOctets = current.InOctets.Value, OutOctets = current.OutOctets.Value, Timestamp = current.Timestamp, SpeedBitsPerSecond = current.SpeedBitsPerSecond });

        if (rate.Unavailable || rate.CounterReset)
            return null;

        return new BandwidthItem(
            deviceId,
            current.Name ?? interfaceIndex.ToString(),
            rate.ReceiveBitsPerSecond is { } rx ? rx / 1_000_000.0 : null,
            rate.TransmitBitsPerSecond is { } tx ? tx / 1_000_000.0 : null,
            rate.ReceiveUtilizationPercent ?? rate.TransmitUtilizationPercent,
            current.Timestamp);
    }

    private HealthSummary ComputeHealthSummary(IReadOnlyList<MonitoringSnapshot> statuses, DateTimeOffset now)
    {
        var healthy = 0;
        var degraded = 0;
        var unhealthy = 0;
        var unknown = 0;
        var stale = 0;

        foreach (var snapshot in statuses)
        {
            if (DashboardCalculators.IsStale(LatestCheck(snapshot), now, _options.StalenessThreshold))
            {
                stale++;
                continue;
            }

            switch (snapshot.Health)
            {
                case NetworkHealthStatus.Healthy: healthy++; break;
                case NetworkHealthStatus.Degraded: degraded++; break;
                case NetworkHealthStatus.Unhealthy: unhealthy++; break;
                default: unknown++; break;
            }
        }

        var running = _monitoringRunning?.Invoke() ?? false;
        return new HealthSummary(healthy, degraded, unhealthy, unknown, stale,
            _alerts.GetActiveAlerts().Count, statuses.Count, running, now);
    }

    private async Task<IReadOnlyList<DeviceHealthRow>> BuildDeviceRowsAsync(
        IReadOnlyList<MonitoringSnapshot> statuses, IReadOnlyDictionary<string, int> alertCounts, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rows = new List<DeviceHealthRow>(statuses.Count);

        foreach (var snapshot in statuses)
        {
            var target = _targets?.GetValueOrDefault(snapshot.TargetId);
            var lastCheck = LatestCheck(snapshot);
            var stale = DashboardCalculators.IsStale(lastCheck, now, _options.StalenessThreshold);
            alertCounts.TryGetValue(snapshot.TargetId, out var alertCount);

            bool? snmpAvailable = null;
            if (_snmp is not null)
            {
                var device = await _snmp.GetLatestDeviceTelemetryAsync(snapshot.TargetId, cancellationToken).ConfigureAwait(false);
                snmpAvailable = device is { Reachable: true };
            }

            rows.Add(new DeviceHealthRow(
                snapshot.TargetId,
                snapshot.DisplayName,
                target?.Type.ToString() ?? "—",
                target?.Address ?? "—",
                stale ? "Stale" : snapshot.Health.ToString(),
                snmpAvailable,
                alertCount,
                lastCheck,
                stale));
        }

        return rows;
    }

    private async Task<IReadOnlyList<RecentEvent>> BuildRecentEventsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var events = new List<RecentEvent>();

        if (_history is not null)
        {
            var transitions = await _history.GetTransitionsAsync(null, now - TimeSpan.FromHours(24), null,
                _options.MaxRecentEvents, 0, cancellationToken).ConfigureAwait(false);

            foreach (var transition in transitions)
            {
                events.Add(new RecentEvent(
                    transition.Timestamp,
                    transition.TargetId,
                    transition.TargetId,
                    RecentEventKind.HealthChanged,
                    transition.NewState.ToString(),
                    $"{transition.TargetId} became {transition.NewState}"));
            }
        }

        foreach (var failure in _monitoring.GetRecentFailures(_options.MaxRecentEvents))
        {
            var kind = failure.CheckType == MonitorCheckType.SnmpTelemetry
                ? RecentEventKind.SnmpPartial
                : RecentEventKind.MonitoringFailure;

            events.Add(new RecentEvent(failure.Timestamp, failure.TargetId, failure.TargetId, kind,
                failure.Status.ToString(), failure.SafeMessage));
        }

        foreach (var alert in _alerts.GetActiveAlerts())
        {
            events.Add(new RecentEvent(alert.FirstSeenAt, alert.TargetId, alert.TargetName,
                RecentEventKind.AlertCreated, alert.Severity.ToString(), alert.Title));
        }

        return events.OrderByDescending(e => e.Timestamp).Take(_options.MaxRecentEvents).ToList();
    }

    private async Task<(InterfaceHealthSummary Summary, IReadOnlyList<InterfaceIssue> Issues)> BuildInterfaceDataAsync(
        CancellationToken cancellationToken)
    {
        if (_snmp is null)
            return (new InterfaceHealthSummary(0, 0, 0, 0, 0), Array.Empty<InterfaceIssue>());

        var interfaces = await _snmp.GetAllLatestInterfaceTelemetryAsync(_options.MaxInterfaces, cancellationToken).ConfigureAwait(false);

        var up = 0;
        var down = 0;
        var adminDown = 0;
        var unknown = 0;
        var issues = new List<InterfaceIssue>();

        foreach (var iface in interfaces)
        {
            if (iface.AdminStatus == SnmpInterfaceAdminStatus.Down)
            {
                adminDown++;
            }
            else
            {
                switch (iface.OperationalStatus)
                {
                    case SnmpInterfaceOperStatus.Up: up++; break;
                    case SnmpInterfaceOperStatus.Down:
                        down++;
                        issues.Add(new InterfaceIssue(iface.DeviceId, iface.Name ?? iface.InterfaceIndex.ToString(),
                            iface.InterfaceIndex, InterfaceIssueKind.OperationalDown, "Operational DOWN"));
                        break;
                    default: unknown++; break;
                }
            }

            var errors = (iface.InErrors ?? 0) + (iface.OutErrors ?? 0);
            if (errors > 0)
            {
                issues.Add(new InterfaceIssue(iface.DeviceId, iface.Name ?? iface.InterfaceIndex.ToString(),
                    iface.InterfaceIndex, InterfaceIssueKind.Errors, $"{errors} error(s)"));
            }

            var discards = (iface.InDiscards ?? 0) + (iface.OutDiscards ?? 0);
            if (discards > 0)
            {
                issues.Add(new InterfaceIssue(iface.DeviceId, iface.Name ?? iface.InterfaceIndex.ToString(),
                    iface.InterfaceIndex, InterfaceIssueKind.Discards, $"{discards} discard(s)"));
            }
        }

        return (new InterfaceHealthSummary(up, down, adminDown, unknown, interfaces.Count), issues);
    }

    private static IReadOnlyList<LatencyItem> BuildLatency(IReadOnlyList<MonitoringSnapshot> statuses)
    {
        var items = new List<LatencyItem>();

        foreach (var snapshot in statuses)
        {
            var ping = DashboardCalculators.LatestPing(snapshot);
            if (ping is null)
                continue;

            items.Add(new LatencyItem(snapshot.TargetId, snapshot.DisplayName,
                ping.AverageLatencyMilliseconds, ping.PacketLossPercent));
        }

        return items;
    }

    private static IReadOnlyList<DeviceHealthRow> ApplyDeviceFilter(
        IReadOnlyList<DeviceHealthRow> devices, string? statusFilter, string? search)
    {
        if (!string.IsNullOrWhiteSpace(statusFilter))
            devices = devices.Where(d => string.Equals(d.Health, statusFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            devices = devices.Where(d =>
                d.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || d.DeviceId.Contains(search, StringComparison.OrdinalIgnoreCase)
                || d.Address.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return devices;
    }

    private static DateTimeOffset LatestCheck(MonitoringSnapshot snapshot)
    {
        if (snapshot.Results.Count == 0)
            return snapshot.Timestamp;

        var latest = snapshot.Results[0].Timestamp;
        foreach (var result in snapshot.Results)
        {
            if (result.Timestamp > latest)
                latest = result.Timestamp;
        }

        return latest;
    }

    private static AlertCard ToAlertCard(Alert alert) => new(
        alert.AlertId,
        alert.Severity.ToString(),
        alert.Status.ToString(),
        alert.Title,
        alert.TargetId,
        alert.TargetName,
        alert.OccurrenceCount,
        alert.FirstSeenAt,
        alert.LastSeenAt);
}

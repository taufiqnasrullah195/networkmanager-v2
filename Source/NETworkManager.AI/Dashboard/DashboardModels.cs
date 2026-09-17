using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Snmp;

namespace NETworkManager.AI.Dashboard;

/// <summary>Dashboard presentation options. Bounded, safe defaults; no secrets.</summary>
public sealed record DashboardOptions
{
    /// <summary>A target is shown STALE when its last check is older than this (default: 2 minutes).</summary>
    public TimeSpan StalenessThreshold { get; init; } = TimeSpan.FromMinutes(2);

    public int MaxRecentEvents { get; init; } = 50;

    public int MaxActiveAlerts { get; init; } = 100;

    public int MaxDevices { get; init; } = 500;

    public int MaxInterfaces { get; init; } = 2000;

    /// <summary>Upper bound on history rows pulled for availability/trend calculations (bounded by design).</summary>
    public int HistoryQueryLimit { get; init; } = 10_000;
}

/// <summary>Top-level network health counts. Derived from current monitoring state, never from alert count alone.</summary>
public sealed record HealthSummary(
    int Healthy,
    int Degraded,
    int Unhealthy,
    int Unknown,
    int Stale,
    int ActiveAlerts,
    int MonitoredDevices,
    bool MonitoringRunning,
    DateTimeOffset GeneratedAt);

/// <summary>One row of the device health table. <see cref="Health"/> is the current state; <see cref="IsStale"/> flags old data.</summary>
public sealed record DeviceHealthRow(
    string DeviceId,
    string Name,
    string Type,
    string Address,
    string Health,
    bool? SnmpAvailable,
    int ActiveAlerts,
    DateTimeOffset? LastCheck,
    bool IsStale);

/// <summary>A compact active-alert card. Secret-free.</summary>
public sealed record AlertCard(
    string AlertId,
    string Severity,
    string Status,
    string Title,
    string TargetId,
    string TargetName,
    int Occurrences,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt);

public enum RecentEventKind
{
    HealthChanged = 0,
    AlertCreated = 1,
    AlertResolved = 2,
    MonitoringFailure = 3,
    SnmpPartial = 4,
}

/// <summary>One recent event with timestamp, target, kind, severity, and evidence summary.</summary>
public sealed record RecentEvent(
    DateTimeOffset Timestamp,
    string TargetId,
    string TargetName,
    RecentEventKind Kind,
    string Severity,
    string Summary);

/// <summary>Interface state counts (RFC 2863). AdminDown is counted separately from operational Down.</summary>
public sealed record InterfaceHealthSummary(int Up, int Down, int AdminDown, int Unknown, int Total);

public enum InterfaceIssueKind
{
    OperationalDown = 0,
    Errors = 1,
    Discards = 2,
}

/// <summary>An interface with a real, telemetry-supported problem. Never fabricated.</summary>
public sealed record InterfaceIssue(
    string DeviceId,
    string InterfaceName,
    int InterfaceIndex,
    InterfaceIssueKind Kind,
    string Summary);

/// <summary>Per-target availability over a window. <see cref="Complete"/> is false when the window was truncated.</summary>
public sealed record AvailabilityItem(
    string TargetId,
    string TargetName,
    double? AvailabilityPercent,
    int HealthyObservations,
    int TotalObservations,
    bool Complete);

/// <summary>Per-target latency summary from current ping telemetry. Null when no ping data exists.</summary>
public sealed record LatencyItem(
    string TargetId,
    string TargetName,
    double? AverageLatencyMs,
    double? PacketLossPercent);

/// <summary>A single computed interface bandwidth sample (rate over the last two samples).</summary>
public sealed record BandwidthItem(
    string DeviceId,
    string InterfaceName,
    double? RxMbps,
    double? TxMbps,
    double? UtilizationPercent,
    DateTimeOffset Timestamp);

public enum TrendHealth { Healthy, Degraded, Unhealthy, Unknown }

/// <summary>One point of a state timeline. A step function, not a continuous numeric series.</summary>
public sealed record HealthTrendPoint(DateTimeOffset Timestamp, TrendHealth Health);

/// <summary>Created/resolved/active alert counts over a window (from persisted alert history).</summary>
public sealed record AlertTrend(int Created, int Resolved, int Active, DateTimeOffset WindowStart, DateTimeOffset WindowEnd);

/// <summary>The aggregated dashboard state. Current state + interfaces + latency; historical metrics are separate queries.</summary>
public sealed record DashboardSnapshot(
    HealthSummary Health,
    IReadOnlyList<DeviceHealthRow> Devices,
    IReadOnlyList<AlertCard> ActiveAlerts,
    IReadOnlyList<RecentEvent> RecentEvents,
    InterfaceHealthSummary Interfaces,
    IReadOnlyList<InterfaceIssue> InterfaceIssues,
    IReadOnlyList<LatencyItem> Latency,
    DateTimeOffset GeneratedAt);

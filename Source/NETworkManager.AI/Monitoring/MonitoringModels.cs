using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     A thing being observed. A target is identified by a stable <see cref="Id"/>; its address may be an IP or a
///     DNS name (never assumed to be an IP). Only read-only checks are ever performed against a target.
/// </summary>
public sealed record MonitoringTarget
{
    /// <summary>Stable, application-unique identifier (e.g. "gw-core", "dns-primary").</summary>
    public required string Id { get; init; }

    public string? Name { get; init; }

    public string? Hostname { get; init; }

    public string? IPAddress { get; init; }

    public MonitorTargetType Type { get; init; } = MonitorTargetType.Host;

    public bool Enabled { get; init; } = true;

    public string? Description { get; init; }

    /// <summary>The address the checks probe: the IP when present, otherwise the hostname.</summary>
    public string? Address => string.IsNullOrWhiteSpace(IPAddress) ? Hostname : IPAddress;

    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? (Address ?? Id)
        : Name;
}

/// <summary>
///     The check-level schedule/scope. <c>null</c> timeout/interval inherit the engine's <see cref="MonitoringOptions"/>.
/// </summary>
public sealed record MonitoringCheck
{
    public required string CheckId { get; init; }

    public required MonitorCheckType Type { get; init; }

    public required string TargetId { get; init; }

    /// <summary>TCP port for <see cref="MonitorCheckType.TcpConnectivity"/> checks; ignored by others.</summary>
    public int Port { get; init; }

    public TimeSpan? Timeout { get; init; }

    public TimeSpan? Interval { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>Human description of what a healthy result looks like (e.g. "TCP connection possible").</summary>
    public string? Expected { get; init; }

    /// <summary>Extension metadata (future SNMP/HTTP checks) — kept opaque and secret-free.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

/// <summary>Structured evidence of one observation — identifies its source so it cannot be invented downstream.</summary>
public sealed record MonitoringEvidence
{
    /// <summary>Where the observation came from (e.g. "NetworkTool.ping").</summary>
    public required string Source { get; init; }

    public required string Summary { get; init; }

    /// <summary>Underlying typed tool output (<see cref="PingResult"/> etc.); opaque to the AI but kept for traceability.</summary>
    public object? Data { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>One completed monitoring check — status, classification, safe message, and observed value.</summary>
public sealed record MonitoringResult
{
    public required string CheckId { get; init; }

    public required string TargetId { get; init; }

    public required MonitorCheckType CheckType { get; init; }

    public required MonitoringResultStatus Status { get; init; }

    public MonitorErrorClass ErrorClassification { get; init; } = MonitorErrorClass.None;

    public required DateTimeOffset Timestamp { get; init; }

    public required TimeSpan Duration { get; init; }

    /// <summary>Secret-free summary suitable for a user/AI (e.g. "ICMP echo reply received.").</summary>
    public required string SafeMessage { get; init; }

    /// <summary>The typed observation (latency, resolved address, port state) when a measurement succeeded.</summary>
    public object? Observed { get; init; }

    public MonitoringEvidence? Evidence { get; init; }

    /// <summary>Correlates a check result to its check (this step: the check id).</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>Current aggregated health of one target at a point in time.</summary>
public sealed record MonitoringSnapshot
{
    public required string TargetId { get; init; }

    public required string DisplayName { get; init; }

    public required NetworkHealthStatus Health { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public IReadOnlyList<MonitoringResult> Results { get; init; } = Array.Empty<MonitoringResult>();
}

/// <summary>A meaningful health-state transition — only raised when the aggregated state actually changes.</summary>
public sealed record HealthStateChange
{
    public required string TargetId { get; init; }

    public required string DisplayName { get; init; }

    public required NetworkHealthStatus Previous { get; init; }

    public required NetworkHealthStatus New { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Evidence/reason for the transition — from the related result, never fabricated.</summary>
    public string? Reason { get; init; }

    public MonitoringResult? RelatedResult { get; init; }
}

/// <summary>A single event published to observers. Carries a state change when the event type is <c>HealthStateChanged</c>.</summary>
public sealed record MonitoringEvent
{
    public required MonitoringEventType Type { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? TargetId { get; init; }

    public string? CheckId { get; init; }

    public string? Message { get; init; }

    public HealthStateChange? StateChange { get; init; }
}
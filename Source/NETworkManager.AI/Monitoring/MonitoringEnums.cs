namespace NETworkManager.AI.Monitoring;

/// <summary>Kind of thing a monitoring target represents.</summary>
public enum MonitorTargetType
{
    Host = 0,
    Server = 1,
    Router = 2,
    Switch = 3,
    NetworkDevice = 4,
    Service = 5,
    Other = 6,
}

/// <summary>The read-only checks a monitoring profile can perform in this step (SNMP/HTTP intentionally absent).</summary>
public enum MonitorCheckType
{
    Ping = 0,
    TcpConnectivity = 1,
    DnsResolution = 2,
}

/// <summary>Status of a single monitoring check result. Failures are NOT collapsed into one bucket.</summary>
public enum MonitoringResultStatus
{
    Unknown = 0,
    Running = 1,
    Healthy = 2,
    Warning = 3,
    Unhealthy = 4,
    Timeout = 5,
    Error = 6,
    Cancelled = 7,
}

/// <summary>Machine-readable classification of why a check is not healthy. Distinct from the coarse status.</summary>
public enum MonitorErrorClass
{
    None = 0,
    NameResolution = 1,
    Unreachable = 2,
    IcmpBlocked = 3,
    TcpConnectivity = 4,
    DnsResolution = 5,
    Timeout = 6,
    Cancelled = 7,
    ExecutionError = 8,
}

/// <summary>Deterministic, aggregated health of one monitored target.</summary>
public enum NetworkHealthStatus
{
    Unknown = 0,
    Healthy = 1,
    Degraded = 2,
    Unhealthy = 3,
}

/// <summary>Lifecycle/transition events published to monitoring observers.</summary>
public enum MonitoringEventType
{
    MonitoringStarted = 0,
    MonitoringStopped = 1,
    MonitoringCompleted = 2,
    MonitoringFailed = 3,
    HealthStateChanged = 4,
}
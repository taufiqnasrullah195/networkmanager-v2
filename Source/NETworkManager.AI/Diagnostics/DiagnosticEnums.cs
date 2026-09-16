namespace NETworkManager.AI.Diagnostics;

/// <summary>Lifecycle status of a single diagnostic step.</summary>
public enum CheckStatus
{
    NotStarted = 0,
    Running = 1,
    Passed = 2,
    Failed = 3,
    Warning = 4,
    Skipped = 5,
    Cancelled = 6,
}

/// <summary>Overall status of a diagnostic run.</summary>
public enum DiagnosticStatus
{
    Unknown = 0,
    Running = 1,
    Success = 2,
    Warning = 3,
    Failed = 4,
    Cancelled = 5,
}

/// <summary>Deterministic failure classifications, derived from observed evidence only (never an unproven root cause).</summary>
public enum FailureClass
{
    None = 0,
    NoNetworkAdapter = 1,
    NoIpAddress = 2,
    InvalidIpConfiguration = 3,
    NoDefaultGateway = 4,
    GatewayUnreachable = 5,
    DnsFailure = 6,
    ExternalConnectivityFailure = 7,
    TcpConnectivityFailure = 8,
    RouteFailure = 9,
    Timeout = 10,
    Unknown = 11,
}

/// <summary>Structured, secret-free diagnostic lifecycle events (for logging).</summary>
public enum DiagnosticLogEvent
{
    DiagnosticStarted,
    DiagnosticStepStarted,
    DiagnosticStepCompleted,
    DiagnosticStepFailed,
    DiagnosticStepSkipped,
    DiagnosticCompleted,
    DiagnosticCancelled,
}
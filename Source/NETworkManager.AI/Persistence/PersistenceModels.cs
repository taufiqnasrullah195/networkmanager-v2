using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Persistence;

/// <summary>One persisted monitoring result (historical evidence).</summary>
public sealed record MonitoringHistoryEntry(
    long Id,
    string TargetId,
    string CheckId,
    MonitorCheckType CheckType,
    MonitoringResultStatus Status,
    MonitorErrorClass ErrorClassification,
    DateTimeOffset Timestamp,
    TimeSpan Duration,
    string? SafeMessage,
    string? ObservedJson,
    string? CorrelationId);

/// <summary>One persisted health-state transition.</summary>
public sealed record StateTransitionEntry(
    long Id,
    string TargetId,
    DateTimeOffset Timestamp,
    NetworkHealthStatus PreviousState,
    NetworkHealthStatus NewState,
    string? Reason);

/// <summary>One persisted alert (full lifecycle).</summary>
public sealed record AlertHistoryEntry(
    string AlertId,
    string Fingerprint,
    string TargetId,
    string? TargetName,
    string? ProfileId,
    AlertSeverity Severity,
    AlertStatus Status,
    string Title,
    string? Description,
    string? Reason,
    string? Evidence,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int OccurrenceCount,
    NetworkHealthStatus PreviousHealthState,
    NetworkHealthStatus CurrentHealthState,
    string? FailureClassification,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionEvidence);

/// <summary>One persisted alert occurrence.</summary>
public sealed record AlertOccurrenceEntry(long Id, string AlertId, DateTimeOffset Timestamp, string? Reason);

/// <summary>Configurable retention windows for history cleanup.</summary>
public sealed record RetentionPolicy
{
    public TimeSpan MonitoringResultRetention { get; init; } = TimeSpan.FromDays(30);
    public TimeSpan StateTransitionRetention { get; init; } = TimeSpan.FromDays(90);
    public TimeSpan ResolvedAlertRetention { get; init; } = TimeSpan.FromDays(180);
    public TimeSpan SnmpDeviceTelemetryRetention { get; init; } = TimeSpan.FromDays(90);
    public TimeSpan SnmpInterfaceTelemetryRetention { get; init; } = TimeSpan.FromDays(30);
}
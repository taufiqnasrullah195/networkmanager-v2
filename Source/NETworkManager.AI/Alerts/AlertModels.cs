using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Alerts;

/// <summary>A structured alert produced from a monitoring health-state change. Secret-free.</summary>
public sealed record Alert
{
    public required string AlertId { get; init; }

    /// <summary>Deterministic dedup key (target + check type) — never display text.</summary>
    public string Fingerprint { get; init; } = string.Empty;

    public required string TargetId { get; init; }

    public string TargetName { get; init; } = string.Empty;

    public string? ProfileId { get; init; }

    public string? ProfileName { get; init; }

    public required AlertSeverity Severity { get; init; }

    public required AlertStatus Status { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public string? Reason { get; init; }

    public string? Evidence { get; init; }

    public required DateTimeOffset FirstSeenAt { get; init; }

    public required DateTimeOffset LastSeenAt { get; init; }

    public int OccurrenceCount { get; init; } = 1;

    public required NetworkHealthStatus PreviousHealthState { get; init; }

    public required NetworkHealthStatus CurrentHealthState { get; init; }

    public string? FailureClassification { get; init; }

    public DateTimeOffset? AcknowledgedAt { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public string? ResolutionEvidence { get; init; }
}

/// <summary>The evaluator's deterministic decision for one <see cref="HealthStateChange"/>.</summary>
public sealed record AlertDecision
{
    public required AlertDecisionKind Kind { get; init; }

    public AlertSeverity Severity { get; init; } = AlertSeverity.Info;

    public string? RuleId { get; init; }
}

/// <summary>A lifecycle event published to alert observers. Carries the alert and the change type.</summary>
public sealed record AlertEvent
{
    public required AlertEventType Type { get; init; }

    public required Alert Alert { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>Alert-engine configuration. Safe defaults; deterministic; no credentials.</summary>
public sealed record AlertOptions
{
    public bool AlertingEnabled { get; init; } = true;

    public bool CreateAlertOnDegraded { get; init; } = true;

    public bool CreateAlertOnUnhealthy { get; init; } = true;

    public bool AutoResolveOnRecovery { get; init; } = true;

    public bool DeduplicationEnabled { get; init; } = true;
}

/// <summary>Read-only input for the <c>network_alerts</c> AI tool.</summary>
public sealed record NetworkAlertsInput : IValidatableToolInput
{
    public string? TargetId { get; init; }

    public int MaxAlerts { get; init; } = 20;

    public bool IncludeResolved { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MaxAlerts is < 0 or > 1000)
            errors.Add("MaxAlerts must be between 0 and 1000.");
        return errors;
    }
}

/// <summary>Structured, secret-free alert information exposed to the AI (facts + evidence only — no inference).</summary>
public sealed record NetworkAlertInfo(
    string AlertId,
    string TargetId,
    string TargetName,
    string Severity,
    string Status,
    string Title,
    string Reason,
    string Evidence,
    string PreviousHealthState,
    string CurrentHealthState,
    string? FailureClassification,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int OccurrenceCount);

/// <summary>Output of <c>network_alerts</c> — structured alert evidence only.</summary>
public sealed record NetworkAlertsResult(
    IReadOnlyList<NetworkAlertInfo> Alerts,
    DateTimeOffset GeneratedAt);
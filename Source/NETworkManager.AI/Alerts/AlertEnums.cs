namespace NETworkManager.AI.Alerts;

/// <summary>Deterministic alert severity — never an arbitrary AI judgment.</summary>
public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3,
}

/// <summary>Lifecycle status of an alert.</summary>
public enum AlertStatus
{
    Open = 0,
    Acknowledged = 1,
    Resolved = 2,
    Suppressed = 3,
}

/// <summary>Alert lifecycle events published to observers.</summary>
public enum AlertEventType
{
    AlertCreated = 0,
    AlertUpdated = 1,
    AlertAcknowledged = 2,
    AlertResolved = 3,
}

/// <summary>What the evaluator decided for a health-state change.</summary>
public enum AlertDecisionKind
{
    /// <summary>Create a new alert (deduplicated into an update when an active alert with the same fingerprint exists).</summary>
    Create = 0,

    /// <summary>Resolve the active alert(s) for the target (evidence-driven recovery).</summary>
    Resolve = 1,

    /// <summary>Do nothing.</summary>
    Ignore = 2,
}
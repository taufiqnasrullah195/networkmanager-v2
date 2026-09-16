using NETworkManager.AI.Models;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     Externalized monitoring configuration: engine-level defaults plus the named set of targets and checks.
///     Contains no secrets (no credentials, tokens, or keys).
/// </summary>
public sealed record MonitoringOptions
{
    public TimeSpan DefaultInterval { get; init; } = TimeSpan.FromSeconds(30);

    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public int MaxConcurrency { get; init; } = 5;

    public int MaxRecentFailures { get; init; } = 100;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (DefaultInterval < TimeSpan.FromSeconds(1) || DefaultInterval > TimeSpan.FromHours(24))
            errors.Add("DefaultInterval must be between 1 second and 24 hours.");
        if (DefaultTimeout < TimeSpan.FromMilliseconds(100) || DefaultTimeout > TimeSpan.FromMinutes(10))
            errors.Add("DefaultTimeout must be between 100 ms and 10 minutes.");
        if (MaxConcurrency is < 1 or > 64)
            errors.Add("MaxConcurrency must be between 1 and 64.");
        return errors;
    }
}

/// <summary>A named monitoring profile: targets + checks + scheduling defaults. Extensible for future SNMP/HTTP checks.</summary>
public sealed record MonitoringProfile
{
    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public IReadOnlyList<MonitoringTarget> Targets { get; init; } = Array.Empty<MonitoringTarget>();

    public IReadOnlyList<MonitoringCheck> Checks { get; init; } = Array.Empty<MonitoringCheck>();

    public TimeSpan? DefaultInterval { get; init; }

    public TimeSpan? DefaultTimeout { get; init; }

    public int? MaxConcurrency { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("Profile name must not be empty.");

        var targetIds = new HashSet<string>(Targets.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in Targets.GroupBy(t => t.Id).Where(g => g.Count() > 1))
            errors.Add($"Duplicate target id '{duplicate.Key}'.");

        var checkIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in Checks)
        {
            if (checkIds.Contains(check.CheckId))
                errors.Add($"Duplicate check id '{check.CheckId}'.");

            checkIds.Add(check.CheckId);

            if (!targetIds.Contains(check.TargetId))
                errors.Add($"Check '{check.CheckId}' references unknown target '{check.TargetId}'.");

            if (check.Type == MonitorCheckType.TcpConnectivity && check.Port is < 1 or > 65535)
                errors.Add($"Check '{check.CheckId}': TCP port must be between 1 and 65535.");
        }

        return errors;
    }
}

/// <summary>Input for the read-only <c>network_monitoring_status</c> tool (optional target filter + failure count).</summary>
public sealed record MonitoringStatusInput : IValidatableToolInput
{
    public string? TargetId { get; init; }

    public int MaxFailures { get; init; } = 10;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (MaxFailures is < 0 or > 1000)
            errors.Add("MaxFailures must be between 0 and 1000.");
        return errors;
    }
}

/// <summary>Per-check status line inside <see cref="MonitoringTargetStatus"/>.</summary>
public sealed record MonitoringCheckStatus(
    string CheckType,
    string Status,
    string? Observed,
    DateTimeOffset? LastCheck);

/// <summary>Structured, secret-free status of one monitored target.</summary>
public sealed record MonitoringTargetStatus(
    string TargetId,
    string DisplayName,
    string Health,
    IReadOnlyList<MonitoringCheckStatus> Checks);

/// <summary>A recent monitoring failure, as returned to the AI/UI.</summary>
public sealed record MonitoringFailure(
    string TargetId,
    string DisplayName,
    string CheckType,
    string Status,
    string SafeMessage,
    DateTimeOffset Timestamp);

/// <summary>Output of <c>network_monitoring_status</c> — structured monitoring evidence only.</summary>
public sealed record MonitoringStatusResult(
    IReadOnlyList<MonitoringTargetStatus> Targets,
    IReadOnlyList<MonitoringFailure> RecentFailures,
    DateTimeOffset GeneratedAt);
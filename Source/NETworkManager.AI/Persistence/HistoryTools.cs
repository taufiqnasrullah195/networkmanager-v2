using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.AI.Persistence;

namespace NETworkManager.AI.Persistence;

/// <summary>Read-only input for the <c>network_monitoring_history</c> AI tool.</summary>
public sealed record MonitoringHistoryInput : IValidatableToolInput
{
    public string? TargetId { get; init; }

    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public int Limit { get; init; } = 50;

    public int Offset { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Limit is < 1 or > 500)
            errors.Add("Limit must be between 1 and 500.");
        if (Offset < 0)
            errors.Add("Offset must not be negative.");
        return errors;
    }
}

/// <summary>Structured, secret-free monitoring history point (facts only — no inference).</summary>
public sealed record MonitoringHistoryPoint(
    string TargetId,
    string CheckType,
    string Status,
    DateTimeOffset Timestamp,
    long DurationMs,
    string? Message);

/// <summary>Structured, secret-free state-transition point.</summary>
public sealed record StateTransitionPoint(
    string TargetId,
    DateTimeOffset Timestamp,
    string PreviousState,
    string NewState);

/// <summary>Output of <c>network_monitoring_history</c>.</summary>
public sealed record MonitoringHistoryResult(
    IReadOnlyList<MonitoringHistoryPoint> Results,
    IReadOnlyList<StateTransitionPoint> Transitions,
    DateTimeOffset GeneratedAt);

/// <summary>Read-only input for the <c>network_alert_history</c> AI tool.</summary>
public sealed record AlertHistoryInput : IValidatableToolInput
{
    public string? TargetId { get; init; }

    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public int Limit { get; init; } = 50;

    public int Offset { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Limit is < 1 or > 500)
            errors.Add("Limit must be between 1 and 500.");
        if (Offset < 0)
            errors.Add("Offset must not be negative.");
        return errors;
    }
}

/// <summary>Structured, secret-free alert history point (facts only).</summary>
public sealed record AlertHistoryPoint(
    string AlertId,
    string TargetId,
    string TargetName,
    string Severity,
    string Status,
    string Title,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int OccurrenceCount,
    string? ResolvedAt,
    string? Reason);

/// <summary>Output of <c>network_alert_history</c>.</summary>
public sealed record AlertHistoryResult(IReadOnlyList<AlertHistoryPoint> Alerts, DateTimeOffset GeneratedAt);

/// <summary><c>network_monitoring_history</c> — read-only historical monitoring evidence for the AI.</summary>
public sealed class MonitoringHistoryTool : INetworkTool
{
    private readonly IMonitoringHistoryRepository _repository;

    public MonitoringHistoryTool(IMonitoringHistoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public string Name => "network_monitoring_history";
    public string Description => "Reads historical monitoring results and health-state transitions (read-only).";
    public ToolCategory Category => ToolCategory.Monitoring;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public Type InputType => typeof(MonitoringHistoryInput);
    public Type OutputType => typeof(MonitoringHistoryResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = input as MonitoringHistoryInput ?? new MonitoringHistoryInput();

        var results = await _repository.GetResultsAsync(
            query.TargetId, query.Start, query.End, query.Limit, query.Offset, cancellationToken).ConfigureAwait(false);

        var transitions = await _repository.GetTransitionsAsync(
            query.TargetId, query.Start, query.End, query.Limit, query.Offset, cancellationToken).ConfigureAwait(false);

        return ToolOutcome.Ok(new MonitoringHistoryResult(
            results.Select(r => new MonitoringHistoryPoint(
                r.TargetId, r.CheckType.ToString(), r.Status.ToString(), r.Timestamp, (long)r.Duration.TotalMilliseconds, r.SafeMessage)).ToList(),
            transitions.Select(t => new StateTransitionPoint(
                t.TargetId, t.Timestamp, t.PreviousState.ToString(), t.NewState.ToString())).ToList(),
            DateTimeOffset.UtcNow));
    }
}

/// <summary><c>network_alert_history</c> — read-only historical alert evidence for the AI.</summary>
public sealed class AlertHistoryTool : INetworkTool
{
    private readonly IAlertHistoryRepository _repository;

    public AlertHistoryTool(IAlertHistoryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public string Name => "network_alert_history";
    public string Description => "Reads historical alerts and their occurrence/lifecycle data (read-only).";
    public ToolCategory Category => ToolCategory.Alerts;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(10);
    public Type InputType => typeof(AlertHistoryInput);
    public Type OutputType => typeof(AlertHistoryResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = input as AlertHistoryInput ?? new AlertHistoryInput();

        var alerts = await _repository.GetAlertHistoryAsync(
            query.TargetId, query.Start, query.End, query.Limit, query.Offset, cancellationToken).ConfigureAwait(false);

        return ToolOutcome.Ok(new AlertHistoryResult(
            alerts.Select(a => new AlertHistoryPoint(
                a.AlertId, a.TargetId, a.TargetName ?? a.TargetId, a.Severity.ToString(), a.Status.ToString(), a.Title,
                a.FirstSeenAt, a.LastSeenAt, a.OccurrenceCount,
                a.ResolvedAt?.ToString("O"), a.Reason)).ToList(),
            DateTimeOffset.UtcNow));
    }
}
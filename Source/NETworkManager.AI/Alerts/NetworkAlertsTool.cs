using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Alerts;

/// <summary>
///     <c>network_alerts</c> — read-only tool exposing structured alert evidence to the AI. It can never
///     acknowledge/resolve/mutate alerts (it depends solely on <see cref="IAlertQuery"/>).
/// </summary>
public sealed class NetworkAlertsTool : INetworkTool
{
    private readonly IAlertQuery _query;

    public NetworkAlertsTool(IAlertQuery query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public string Name => "network_alerts";
    public string Description => "Reads active and recent monitoring alerts (read-only).";
    public ToolCategory Category => ToolCategory.Alerts;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public Type InputType => typeof(NetworkAlertsInput);
    public Type OutputType => typeof(NetworkAlertsResult);

    public Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = input as NetworkAlertsInput ?? new NetworkAlertsInput();

        IEnumerable<Alert> alerts = query.IncludeResolved
            ? _query.GetRecentAlerts(query.MaxAlerts)
            : _query.GetActiveAlerts();

        if (!string.IsNullOrWhiteSpace(query.TargetId))
            alerts = alerts.Where(a => string.Equals(a.TargetId, query.TargetId, StringComparison.OrdinalIgnoreCase));

        var list = alerts
            .Take(Math.Max(query.MaxAlerts, 0))
            .Select(ToInfo)
            .ToList();

        return Task.FromResult(ToolOutcome.Ok(new NetworkAlertsResult(list, DateTimeOffset.UtcNow)));
    }

    private static NetworkAlertInfo ToInfo(Alert alert) => new(
        alert.AlertId,
        alert.TargetId,
        alert.TargetName,
        alert.Severity.ToString(),
        alert.Status.ToString(),
        alert.Title,
        alert.Reason ?? string.Empty,
        alert.Evidence ?? string.Empty,
        alert.PreviousHealthState.ToString(),
        alert.CurrentHealthState.ToString(),
        alert.FailureClassification,
        alert.FirstSeenAt,
        alert.LastSeenAt,
        alert.OccurrenceCount);
}
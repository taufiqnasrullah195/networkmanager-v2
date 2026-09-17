using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Notifications;

/// <summary>Input for the read-only <c>network_notification_history</c> tool.</summary>
public sealed record NotificationHistoryInput : IValidatableToolInput
{
    public string? AlertId { get; init; }

    public string? Channel { get; init; }

    public string? Status { get; init; }

    public int Limit { get; init; } = 50;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Limit is < 1 or > 1000)
            errors.Add("Limit must be between 1 and 1000.");
        if (Status is not null && !Enum.TryParse<NotificationStatus>(Status, ignoreCase: true, out _))
            errors.Add($"Unknown notification status '{Status}'.");
        return errors;
    }
}

/// <summary>Structured, secret-free notification history exposed to the AI (facts only).</summary>
public sealed record NotificationInfo(
    string NotificationId,
    string AlertId,
    string Channel,
    string Status,
    string EventType,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    int AttemptCount,
    string? FailureReason);

/// <summary>Output of <c>network_notification_history</c>.</summary>
public sealed record NotificationHistoryResult(IReadOnlyList<NotificationInfo> Notifications, DateTimeOffset GeneratedAt);

/// <summary>
///     <c>network_notification_history</c> — read-only tool exposing notification delivery history. Depends only on
///     <see cref="INotificationQuery"/>; it cannot send a notification, change configuration, or modify rules.
/// </summary>
public sealed class NotificationHistoryTool : INetworkTool
{
    private readonly INotificationQuery _query;

    public NotificationHistoryTool(INotificationQuery query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public string Name => "network_notification_history";
    public string Description => "Reads notification delivery history (channel, status, event type, attempts) for alerts (read-only).";
    public ToolCategory Category => ToolCategory.Alerts;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public Type InputType => typeof(NotificationHistoryInput);
    public Type OutputType => typeof(NotificationHistoryResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = input as NotificationHistoryInput ?? new NotificationHistoryInput();

        var status = query.Status is null
            ? (NotificationStatus?)null
            : Enum.Parse<NotificationStatus>(query.Status, ignoreCase: true);

        var notifications = await _query.GetHistoryAsync(query.AlertId, query.Channel, status, null, null,
            query.Limit, 0, cancellationToken).ConfigureAwait(false);

        var items = notifications
            .Select(n => new NotificationInfo(
                n.NotificationId, n.AlertId, n.Channel, n.Status.ToString(), n.EventType.ToString(),
                n.CreatedAt, n.SentAt, n.AttemptCount, n.FailureReason))
            .ToList();

        return ToolOutcome.Ok(new NotificationHistoryResult(items, DateTimeOffset.UtcNow));
    }
}

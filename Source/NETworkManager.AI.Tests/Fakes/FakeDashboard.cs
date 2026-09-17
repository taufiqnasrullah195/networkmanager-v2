using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Models;
using NETworkManager.AI.Persistence;

namespace NETworkManager.AI.Tests.Fakes;

public sealed class FakeMonitoringQuery : IMonitoringQuery
{
    public IReadOnlyList<MonitoringSnapshot> Statuses { get; set; } = Array.Empty<MonitoringSnapshot>();

    public IReadOnlyList<MonitoringResult> Failures { get; set; } = Array.Empty<MonitoringResult>();

    public IReadOnlyList<MonitoringSnapshot> GetCurrentStatus() => Statuses;

    public NetworkHealthStatus? GetHealth(string targetId) =>
        Statuses.FirstOrDefault(s => s.TargetId == targetId)?.Health;

    public MonitoringResult? GetLatest(string targetId, MonitorCheckType type) => null;

    public IReadOnlyList<MonitoringResult> GetRecentFailures(int maxCount) => Failures.Take(maxCount).ToList();
}

public sealed class FakeAlertQuery : IAlertQuery
{
    public IReadOnlyList<Alert> Active { get; set; } = Array.Empty<Alert>();

    public IReadOnlyList<Alert> Recent { get; set; } = Array.Empty<Alert>();

    public IReadOnlyList<Alert> GetActiveAlerts() => Active;

    public Alert? GetAlert(string alertId) => Active.FirstOrDefault(a => a.AlertId == alertId);

    public IReadOnlyList<Alert> GetRecentAlerts(int maxCount) => Recent.Take(maxCount).ToList();
}

public sealed class FakeMonitoringHistoryRepository : IMonitoringHistoryRepository
{
    public IReadOnlyList<MonitoringHistoryEntry> Results { get; set; } = Array.Empty<MonitoringHistoryEntry>();

    public IReadOnlyList<StateTransitionEntry> Transitions { get; set; } = Array.Empty<StateTransitionEntry>();

    public Task<IReadOnlyList<MonitoringHistoryEntry>> GetResultsAsync(string? targetId, DateTimeOffset? start,
        DateTimeOffset? end, int limit, int offset, CancellationToken cancellationToken = default) =>
        Task.FromResult(Results);

    public Task<IReadOnlyList<StateTransitionEntry>> GetTransitionsAsync(string? targetId, DateTimeOffset? start,
        DateTimeOffset? end, int limit, int offset, CancellationToken cancellationToken = default) =>
        Task.FromResult(Transitions);
}

public sealed class FakeAlertHistoryRepository : IAlertHistoryRepository
{
    public IReadOnlyList<AlertHistoryEntry> Alerts { get; set; } = Array.Empty<AlertHistoryEntry>();

    public IReadOnlyList<AlertOccurrenceEntry> Occurrences { get; set; } = Array.Empty<AlertOccurrenceEntry>();

    public Task<IReadOnlyList<AlertHistoryEntry>> GetAlertHistoryAsync(string? targetId, DateTimeOffset? start,
        DateTimeOffset? end, int limit, int offset, CancellationToken cancellationToken = default) =>
        Task.FromResult(Alerts);

    public Task<IReadOnlyList<AlertOccurrenceEntry>> GetOccurrencesAsync(string alertId, int limit, int offset,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Occurrences);
}

/// <summary>Builders for dashboard test data (snapshots, results, alerts, history entries).</summary>
public static class DashboardTestData
{
    public static MonitoringSnapshot Snapshot(string id, NetworkHealthStatus health, DateTimeOffset? ts = null,
        params MonitoringResult[] results) => new()
    {
        TargetId = id,
        DisplayName = id,
        Health = health,
        Timestamp = ts ?? DateTimeOffset.UtcNow,
        Results = results,
    };

    public static MonitoringResult PingCheck(string targetId, double avgMs, double loss = 0, DateTimeOffset? ts = null) => new()
    {
        CheckId = $"{targetId}-ping",
        TargetId = targetId,
        CheckType = MonitorCheckType.Ping,
        Status = MonitoringResultStatus.Healthy,
        Timestamp = ts ?? DateTimeOffset.UtcNow,
        Duration = TimeSpan.FromMilliseconds(avgMs),
        SafeMessage = "ICMP echo reply received.",
        Observed = new PingResult
        {
            Target = targetId,
            Success = true,
            Sent = 4,
            Received = 4,
            AverageLatencyMilliseconds = avgMs,
            PacketLossPercent = loss,
        },
    };

    public static Alert ActiveAlert(string id, string targetId, AlertSeverity severity = AlertSeverity.Error,
        DateTimeOffset? firstSeen = null, AlertStatus status = AlertStatus.Open) => new()
    {
        AlertId = id,
        TargetId = targetId,
        TargetName = targetId,
        Severity = severity,
        Status = status,
        Title = $"{targetId} unhealthy",
        FirstSeenAt = firstSeen ?? DateTimeOffset.UtcNow,
        LastSeenAt = firstSeen ?? DateTimeOffset.UtcNow,
        OccurrenceCount = 1,
        PreviousHealthState = NetworkHealthStatus.Healthy,
        CurrentHealthState = NetworkHealthStatus.Unhealthy,
    };

    public static MonitoringHistoryEntry ResultEntry(string targetId, MonitoringResultStatus status,
        DateTimeOffset ts, long id = 1) => new(id, targetId, $"{targetId}-ping", MonitorCheckType.Ping, status,
        MonitorErrorClass.None, ts, TimeSpan.FromSeconds(1), "ok", null, $"{targetId}-ping");

    public static StateTransitionEntry Transition(string targetId, NetworkHealthStatus from, NetworkHealthStatus to,
        DateTimeOffset ts, long id = 1) => new(id, targetId, ts, from, to, null);

    public static AlertHistoryEntry AlertEntry(string id, string targetId, DateTimeOffset firstSeen,
        DateTimeOffset? resolved = null) => new(id, $"{targetId}\u0001ping", targetId, targetId, null,
        AlertSeverity.Error, resolved is null ? AlertStatus.Open : AlertStatus.Resolved, "unhealthy", null, null, null,
        firstSeen, firstSeen, 1, NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy, null, null, resolved, null);
}

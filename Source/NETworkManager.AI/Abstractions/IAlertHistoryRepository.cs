using NETworkManager.AI.Persistence;

namespace NETworkManager.AI.Abstractions;

/// <summary>Read-only alert history queries (bounded).</summary>
public interface IAlertHistoryRepository
{
    Task<IReadOnlyList<AlertHistoryEntry>> GetAlertHistoryAsync(
        string? targetId, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlertOccurrenceEntry>> GetOccurrencesAsync(
        string alertId, int limit, int offset, CancellationToken cancellationToken = default);
}
using NETworkManager.AI.Persistence;

namespace NETworkManager.AI.Abstractions;

/// <summary>Read-only monitoring history queries (bounded).</summary>
public interface IMonitoringHistoryRepository
{
    Task<IReadOnlyList<MonitoringHistoryEntry>> GetResultsAsync(
        string? targetId, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StateTransitionEntry>> GetTransitionsAsync(
        string? targetId, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default);
}
using NETworkManager.AI.Persistence;

namespace NETworkManager.AI.Abstractions;

/// <summary>Deletes expired history records while preserving active/current data. Bounded, cancellable, safe at shutdown.</summary>
public interface IDataRetentionService
{
    /// <summary>Removes expired records and returns the number deleted.</summary>
    Task<int> RunCleanupAsync(RetentionPolicy policy, CancellationToken cancellationToken = default);
}
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>Persistence boundary for monitoring profiles. The UI never sees this; a future non-JSON backend replaces it.</summary>
public interface IMonitoringProfileRepository
{
    /// <summary>Returns the catalog, or <c>null</c> when no configuration exists. Throws on malformed/corrupted data.</summary>
    Task<MonitoringProfileCatalog?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(MonitoringProfileCatalog catalog, CancellationToken cancellationToken = default);
}
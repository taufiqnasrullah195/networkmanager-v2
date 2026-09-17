using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>In-memory <see cref="IMonitoringProfileRepository"/> for pure service tests (no filesystem).</summary>
public sealed class InMemoryMonitoringProfileRepository : IMonitoringProfileRepository
{
    private MonitoringProfileCatalog? _catalog;

    public int SaveCount { get; private set; }

    public Task<MonitoringProfileCatalog?> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_catalog);

    public Task SaveAsync(MonitoringProfileCatalog catalog, CancellationToken cancellationToken = default)
    {
        _catalog = catalog;
        SaveCount++;
        return Task.CompletedTask;
    }
}
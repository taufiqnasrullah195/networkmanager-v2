namespace NETworkManager.AI.Monitoring;

/// <summary>Versioned set of monitoring profiles persisted together (a "catalog").</summary>
public sealed record MonitoringProfileCatalog
{
    public int Version { get; init; } = 1;

    public IReadOnlyList<MonitoringProfile> Profiles { get; init; } = Array.Empty<MonitoringProfile>();
}
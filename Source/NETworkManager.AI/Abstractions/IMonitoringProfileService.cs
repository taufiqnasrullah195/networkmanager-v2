using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Abstractions;

/// <summary>Owns monitoring profile configuration (create/read/update/delete + validation). The UI depends on this, never on JSON.</summary>
public interface IMonitoringProfileService
{
    /// <summary>Loads configuration from the repository (malformed data is handled safely and reported via <see cref="LastError"/>).</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MonitoringProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);

    Task<MonitoringProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default);

    Task<MonitoringProfile> CreateProfileAsync(MonitoringProfile profile, CancellationToken cancellationToken = default);

    Task<MonitoringProfile> UpdateProfileAsync(MonitoringProfile profile, CancellationToken cancellationToken = default);

    Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> EnableProfileAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DisableProfileAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Validates a profile without mutating anything. Empty result = valid.</summary>
    IReadOnlyList<string> ValidateProfile(MonitoringProfile profile);

    /// <summary>Human-readable last load/parse error, or <c>null</c> when configuration loaded cleanly.</summary>
    string? LastError { get; }
}
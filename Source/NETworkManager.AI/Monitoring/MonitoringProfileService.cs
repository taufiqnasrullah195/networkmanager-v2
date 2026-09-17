using System.Text.Json;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     In-memory profile service over a repository. Validates before every mutation, enforces unique profile
///     names/ids, persists atomically, and loads malformed data safely (reported via <see cref="LastError"/>).
/// </summary>
public sealed class MonitoringProfileService : IMonitoringProfileService
{
    private readonly IMonitoringProfileRepository _repository;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly List<MonitoringProfile> _profiles = new();
    private string? _lastError;

    public MonitoringProfileService(IMonitoringProfileRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
                return _lastError;
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _profiles.Clear();
            _lastError = null;

            MonitoringProfileCatalog? catalog;

            try
            {
                catalog = await _repository.LoadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
            {
                _lastError = "Monitoring configuration could not be loaded and has been reset.";
                return;
            }

            if (catalog is null)
                return;

            foreach (var profile in catalog.Profiles)
            {
                var errors = ValidateProfile(profile);

                if (errors.Count > 0)
                {
                    _lastError = $"Profile '{profile.EffectiveId}' has invalid configuration and was skipped.";
                    continue;
                }

                if (_profiles.Any(p => string.Equals(p.EffectiveId, profile.EffectiveId, StringComparison.OrdinalIgnoreCase)))
                {
                    _lastError = $"Duplicate profile '{profile.EffectiveId}' was skipped.";
                    continue;
                }

                // Normalize so every stored profile has a stable Id (backward compatibility with Id-less profiles).
                _profiles.Add(profile with { Id = string.IsNullOrWhiteSpace(profile.Id) ? profile.Name : profile.Id });
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<MonitoringProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _profiles.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MonitoringProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _profiles.FirstOrDefault(p => string.Equals(p.EffectiveId, id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MonitoringProfile> CreateProfileAsync(MonitoringProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = ValidateProfile(profile);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(profile));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var id = profile.EffectiveId;

            if (_profiles.Any(p => string.Equals(p.EffectiveId, id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A profile with the id '{id}' already exists.");

            if (NameTaken(profile.Name, exceptId: null))
                throw new InvalidOperationException($"A profile named '{profile.Name}' already exists.");

            var normalized = profile with { Id = string.IsNullOrWhiteSpace(profile.Id) ? profile.Name : profile.Id };

            _profiles.Add(normalized);
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);

            return normalized;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MonitoringProfile> UpdateProfileAsync(MonitoringProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var errors = ValidateProfile(profile);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(profile));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var id = profile.EffectiveId;
            var index = _profiles.FindIndex(p => string.Equals(p.EffectiveId, id, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                throw new InvalidOperationException($"Profile '{id}' was not found.");

            if (NameTaken(profile.Name, exceptId: id))
                throw new InvalidOperationException($"A profile named '{profile.Name}' already exists.");

            _profiles[index] = profile with { Id = string.IsNullOrWhiteSpace(profile.Id) ? profile.Name : profile.Id };
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);

            return _profiles[index];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var removed = _profiles.RemoveAll(p => string.Equals(p.EffectiveId, id, StringComparison.OrdinalIgnoreCase)) > 0;

            if (removed)
                await SaveLockedAsync(cancellationToken).ConfigureAwait(false);

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<bool> EnableProfileAsync(string id, CancellationToken cancellationToken = default) =>
        SetEnabledAsync(id, enabled: true, cancellationToken);

    public Task<bool> DisableProfileAsync(string id, CancellationToken cancellationToken = default) =>
        SetEnabledAsync(id, enabled: false, cancellationToken);

    public IReadOnlyList<string> ValidateProfile(MonitoringProfile profile) =>
        profile.Validate();

    private async Task<bool> SetEnabledAsync(string id, bool enabled, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var index = _profiles.FindIndex(p => string.Equals(p.EffectiveId, id, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return false;

            _profiles[index] = _profiles[index] with { Enabled = enabled };
            await SaveLockedAsync(cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool NameTaken(string name, string? exceptId) =>
        _profiles.Any(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) &&
            (exceptId is null || !string.Equals(p.EffectiveId, exceptId, StringComparison.OrdinalIgnoreCase)));

    private Task SaveLockedAsync(CancellationToken cancellationToken) =>
        _repository.SaveAsync(new MonitoringProfileCatalog { Version = 1, Profiles = _profiles.ToList() }, cancellationToken);
}
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     JSON catalog persistence (SHA-256 checksummed, versioned). Backward-compatible: migrates the Step 9
///     single-profile file into a catalog on first load. Malformed/tampered data throws <see cref="InvalidDataException"/>
///     (handled by the service) rather than crashing the app.
/// </summary>
public sealed class JsonMonitoringProfileRepository : IMonitoringProfileRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly string? _legacyPath;

    public JsonMonitoringProfileRepository(string path, string? legacyPath = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        _path = path;
        _legacyPath = legacyPath;
    }

    public Task<MonitoringProfileCatalog?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_path))
            return Task.FromResult<MonitoringProfileCatalog?>(ReadCatalog(_path));

        if (_legacyPath is not null && File.Exists(_legacyPath))
        {
            var legacy = ReadLegacyProfile(_legacyPath);
            var catalog = new MonitoringProfileCatalog { Version = 1, Profiles = new[] { legacy } };

            SaveAsync(catalog, cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult<MonitoringProfileCatalog?>(catalog);
        }

        return Task.FromResult<MonitoringProfileCatalog?>(null);
    }

    public Task SaveAsync(MonitoringProfileCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var json = JsonSerializer.Serialize(new CatalogPayload(catalog, ComputeCatalogChecksum(catalog)), JsonOptions);
        var directory = Path.GetDirectoryName(_path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_path, json);
        return Task.CompletedTask;
    }

    private static MonitoringProfileCatalog ReadCatalog(string path)
    {
        var payload = JsonSerializer.Deserialize<CatalogPayload>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Monitoring configuration is empty or malformed.");

        if (!string.Equals(payload.Checksum, ComputeCatalogChecksum(payload.Catalog), StringComparison.Ordinal))
            throw new InvalidDataException("Monitoring configuration has been modified or is corrupted.");

        return payload.Catalog;
    }

    private static MonitoringProfile ReadLegacyProfile(string path)
    {
        // Step 9's MonitoringProfileStore wrote { "profile": {...}, "checksum": "..." }.
        var payload = JsonSerializer.Deserialize<LegacyPayload>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Legacy monitoring configuration is malformed.");

        if (!string.Equals(payload.Checksum, ComputeProfileChecksum(payload.Profile), StringComparison.Ordinal))
            throw new InvalidDataException("Legacy monitoring configuration is corrupted.");

        return payload.Profile;
    }

    private static string ComputeCatalogChecksum(MonitoringProfileCatalog catalog)
    {
        var json = JsonSerializer.Serialize(catalog, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static string ComputeProfileChecksum(MonitoringProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private sealed record CatalogPayload(MonitoringProfileCatalog Catalog, string Checksum);

    private sealed record LegacyPayload(MonitoringProfile Profile, string Checksum);
}
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     File-backed externalization of a <see cref="MonitoringProfile"/> (no secrets — targets/checks/defaults only).
///     JSON with a SHA-256 checksum so a tampered file reads as <c>null</c> instead of loading bad configuration.
/// </summary>
public sealed class MonitoringProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public MonitoringProfileStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        _path = path;
    }

    public MonitoringProfile? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            var payload = JsonSerializer.Deserialize<ProfilePayload>(File.ReadAllText(_path), JsonOptions);

            if (payload?.Profile is null || payload.Checksum != ComputeChecksum(payload.Profile))
                return null;

            return payload.Profile;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(MonitoringProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var payload = new ProfilePayload(profile, ComputeChecksum(profile));
        var directory = Path.GetDirectoryName(_path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static string ComputeChecksum(MonitoringProfile profile)
    {
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private sealed record ProfilePayload(MonitoringProfile Profile, string Checksum);
}
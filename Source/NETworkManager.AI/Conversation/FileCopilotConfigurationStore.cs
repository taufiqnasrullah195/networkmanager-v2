using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Conversation;

/// <summary>
///     File-backed store for the copilot provider configuration (no secrets — credential references only).
///     Lives beside the user's data (path supplied by the caller); JSON with a checksum to detect tampering.
/// </summary>
public sealed class FileCopilotConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;

    public FileCopilotConfigurationStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        _path = path;
    }

    public CopilotProviderConfiguration? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            var payload = JsonSerializer.Deserialize<ConfigPayload>(File.ReadAllText(_path), JsonOptions);

            if (payload?.Configuration is null || payload.Checksum != ComputeChecksum(payload.Configuration))
                return null; // missing or tampered — treat as not configured

            return payload.Configuration;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(CopilotProviderConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var payload = new ConfigPayload(configuration, ComputeChecksum(configuration));
        var directory = Path.GetDirectoryName(_path);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_path, JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static string ComputeChecksum(CopilotProviderConfiguration configuration)
    {
        var json = JsonSerializer.Serialize(configuration, JsonOptions);
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private sealed record ConfigPayload(CopilotProviderConfiguration Configuration, string Checksum);
}
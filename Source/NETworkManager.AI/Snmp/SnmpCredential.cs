using System.Text.Json;
using System.Text.Json.Serialization;

namespace NETworkManager.AI.Snmp;

/// <summary>
///     SNMP credential material (community string, or SNMPv3 username/auth/privacy). This object holds secrets and is
///     therefore NEVER stored in configuration, never logged, and never placed in telemetry or AI context. It is
///     serialized by <see cref="SnmpCredentialCodec"/> into a single secret string kept inside
///     <see cref="NETworkManager.AI.Abstractions.ISecureCredentialStore"/> under a <c>CredentialReference</c> key.
/// </summary>
public sealed record SnmpCredential
{
    public SnmpVersion Version { get; init; } = SnmpVersion.V2C;

    /// <summary>Community string for SNMPv1/v2c.</summary>
    public string? Community { get; init; }

    /// <summary>Username for SNMPv3.</summary>
    public string? Username { get; init; }

    public SnmpSecurityLevel SecurityLevel { get; init; } = SnmpSecurityLevel.NoAuthNoPriv;

    public SnmpAuthAlgorithm AuthAlgorithm { get; init; } = SnmpAuthAlgorithm.Sha1;

    /// <summary>Authentication password for SNMPv3 (authNoPriv/authPriv).</summary>
    public string? AuthPassword { get; init; }

    public SnmpPrivAlgorithm PrivAlgorithm { get; init; } = SnmpPrivAlgorithm.Aes;

    /// <summary>Privacy password for SNMPv3 (authPriv).</summary>
    public string? PrivPassword { get; init; }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Version == SnmpVersion.V3)
        {
            if (string.IsNullOrWhiteSpace(Username))
                errors.Add("SNMPv3 requires a username.");

            if (SecurityLevel >= SnmpSecurityLevel.AuthNoPriv && string.IsNullOrWhiteSpace(AuthPassword))
                errors.Add("SNMPv3 authNoPriv/authPriv requires an authentication password.");

            if (SecurityLevel == SnmpSecurityLevel.AuthPriv && string.IsNullOrWhiteSpace(PrivPassword))
                errors.Add("SNMPv3 authPriv requires a privacy password.");
        }
        else if (string.IsNullOrWhiteSpace(Community))
        {
            errors.Add("SNMPv1/v2c requires a community string.");
        }

        return errors;
    }
}

/// <summary>Serializes/deserializes <see cref="SnmpCredential"/> to/from the single secret string stored in secure storage.</summary>
public static class SnmpCredentialCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(SnmpCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        return JsonSerializer.Serialize(credential, Options);
    }

    /// <summary>Returns the decoded credential, or <c>null</c> when the secret is empty or malformed.</summary>
    public static SnmpCredential? Decode(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
            return null;

        try
        {
            return JsonSerializer.Deserialize<SnmpCredential>(secret, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

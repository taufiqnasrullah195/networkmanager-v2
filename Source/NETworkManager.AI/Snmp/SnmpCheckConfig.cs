namespace NETworkManager.AI.Snmp;

/// <summary>
///     Read-only SNMP check configuration. Contains NO secrets — the credential is referenced by
///     <see cref="CredentialReference"/> and resolved from secure storage at collection time.
/// </summary>
public sealed record SnmpCheckConfig
{
    public SnmpVersion Version { get; init; } = SnmpVersion.V2C;

    /// <summary>UDP port (default SNMP port 161).</summary>
    public int Port { get; init; } = 161;

    /// <summary>Key into <c>ISecureCredentialStore</c> holding the <see cref="SnmpCredential"/>.</summary>
    public string? CredentialReference { get; init; }

    public SnmpCollectionMode CollectionMode { get; init; } = SnmpCollectionMode.SystemAndInterfaces;

    /// <summary>Per-check timeout (null inherits the monitoring engine default).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Bounded retry count (0..5).</summary>
    public int Retries { get; init; } = 1;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Port is < 1 or > 65535)
            errors.Add("SNMP port must be between 1 and 65535.");

        if (string.IsNullOrWhiteSpace(CredentialReference))
            errors.Add("SNMP check requires a credential reference.");

        if (Retries is < 0 or > 5)
            errors.Add("SNMP retries must be between 0 and 5.");

        if (Timeout is { } timeout && (timeout < TimeSpan.FromMilliseconds(100) || timeout > TimeSpan.FromMinutes(10)))
            errors.Add("SNMP timeout must be between 100 ms and 10 minutes.");

        return errors;
    }
}

/// <summary>
///     A resolved SNMP session — connection target plus the credential material needed to speak to the device. This
///     object carries secrets and is confined to the provider/collector boundary; it must never be logged or persisted.
/// </summary>
public sealed record SnmpSession
{
    public required string Host { get; init; }

    public int Port { get; init; } = 161;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public int Retries { get; init; } = 1;

    public required SnmpCredential Credential { get; init; }
}

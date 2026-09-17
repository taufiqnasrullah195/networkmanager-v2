namespace NETworkManager.AI.Snmp;

/// <summary>A single provider-neutral raw SNMP variable (OID → string value). No third-party library types escape the provider.</summary>
public sealed record SnmpVariable(string Oid, string Value);

/// <summary>Typed exception from <see cref="ISnmpProvider"/> so callers can map failures without string matching.</summary>
public sealed class SnmpException : Exception
{
    public SnmpException(SnmpErrorKind kind, string message, Exception? inner = null) : base(message, inner)
    {
        Kind = kind;
    }

    public SnmpErrorKind Kind { get; }
}

/// <summary>Normalized, secret-free device telemetry (system group). Missing OIDs are represented as <c>null</c>, never fabricated.</summary>
public sealed record DeviceTelemetry
{
    public required string DeviceId { get; init; }

    public required string Target { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public string? SysName { get; init; }

    public string? SysDescription { get; init; }

    public string? SysObjectId { get; init; }

    /// <summary>sysUpTime as a duration; <c>null</c> when unavailable. An observation only, not a health claim.</summary>
    public TimeSpan? Uptime { get; init; }

    public int InterfaceCount { get; init; }

    public bool Reachable { get; init; }

    public TimeSpan ResponseTime { get; init; }

    public SnmpCollectionStatus CollectionStatus { get; init; }

    public IReadOnlyList<string> CollectionErrors { get; init; } = Array.Empty<string>();
}

/// <summary>Normalized, secret-free interface telemetry. 64-bit counters preferred; <c>null</c> means "not provided".</summary>
public sealed record InterfaceTelemetry
{
    public required string DeviceId { get; init; }

    public int InterfaceIndex { get; init; }

    /// <summary>ifName (ifXTable) when available, else ifDescr.</summary>
    public string? Name { get; init; }

    /// <summary>ifDescr.</summary>
    public string? Description { get; init; }

    public SnmpInterfaceAdminStatus AdminStatus { get; init; }

    public SnmpInterfaceOperStatus OperationalStatus { get; init; }

    /// <summary>Interface speed in bits/second (ifSpeed / ifHighSpeed); <c>null</c> when unavailable.</summary>
    public ulong? SpeedBitsPerSecond { get; init; }

    public ulong? InOctets { get; init; }

    public ulong? OutOctets { get; init; }

    public ulong? InErrors { get; init; }

    public ulong? OutErrors { get; init; }

    public ulong? InDiscards { get; init; }

    public ulong? OutDiscards { get; init; }

    public bool UsesHighCapacityCounters { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>Result of one SNMP collection: structured telemetry + deterministic status + safe errors.</summary>
public sealed record SnmpCollectionResult
{
    public required string TargetId { get; init; }

    public required string Host { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public SnmpCollectionStatus Status { get; init; }

    public DeviceTelemetry? Device { get; init; }

    public IReadOnlyList<InterfaceTelemetry> Interfaces { get; init; } = Array.Empty<InterfaceTelemetry>();

    /// <summary>Secret-free error messages (never community strings, passwords, or raw stack traces).</summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public TimeSpan ResponseTime { get; init; }
}

namespace NETworkManager.AI.Models;

/// <summary>A single DNS record as returned by <c>dns_lookup</c>.</summary>
public sealed record DnsRecord(string DomainName, int Ttl, string RecordType, string Result);

/// <summary>Result of <c>ping</c>.</summary>
public sealed record PingResult
{
    public required string Target { get; init; }
    public required bool Success { get; init; }
    public int Sent { get; init; }
    public int Received { get; init; }
    /// <summary>Number of probes whose reply was ICMP <c>TimedOut</c> (no reply within the timeout).</summary>
    public int TimedOutCount { get; init; }
    public double PacketLossPercent { get; init; }
    public long MinLatencyMilliseconds { get; init; }
    public long MaxLatencyMilliseconds { get; init; }
    public double AverageLatencyMilliseconds { get; init; }
    public IReadOnlyList<long> RoundTripTimesMilliseconds { get; init; } = Array.Empty<long>();
}

/// <summary>Result of <c>dns_lookup</c>.</summary>
public sealed record DnsLookupResult
{
    public required string Host { get; init; }
    public required bool Success { get; init; }
    public IReadOnlyList<DnsRecord> Records { get; init; } = Array.Empty<DnsRecord>();
    public string? Error { get; init; }
}

/// <summary>Result of <c>tcp_test</c>.</summary>
public sealed record TcpTestResult
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required bool Reachable { get; init; }
    public required string State { get; init; }
}

/// <summary>A single hop discovered by <c>traceroute</c>.</summary>
public sealed record TracerouteHop(int Hop, string? IpAddress, string? Hostname, string Status, long? LatencyMilliseconds);

/// <summary>Result of <c>traceroute</c>.</summary>
public sealed record TracerouteResult
{
    public required string Target { get; init; }
    public required bool Success { get; init; }
    public IReadOnlyList<TracerouteHop> Hops { get; init; } = Array.Empty<TracerouteHop>();
    public string? Error { get; init; }
}

/// <summary>Read-only summary of a local network adapter.</summary>
public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    string Type,
    string Status,
    long SpeedBitsPerSecond,
    string? MacAddress,
    IReadOnlyList<string> IPv4Addresses,
    IReadOnlyList<string> IPv4Gateways,
    bool DhcpEnabled,
    IReadOnlyList<string> DnsServers);

/// <summary>Result of <c>network_adapter_info</c>.</summary>
public sealed record NetworkAdapterResult
{
    public required bool Success { get; init; }
    public IReadOnlyList<NetworkAdapterInfo> Adapters { get; init; } = Array.Empty<NetworkAdapterInfo>();
    public string? Error { get; init; }
}

/// <summary>A single entry in the local IP routing table.</summary>
public sealed record RouteEntry(string DestinationPrefix, int PrefixLength, string NextHop, int InterfaceIndex, uint Metric, string Protocol);

/// <summary>Result of <c>routing_table</c>.</summary>
public sealed record RoutingTableResult
{
    public required bool Success { get; init; }
    public IReadOnlyList<RouteEntry> Routes { get; init; } = Array.Empty<RouteEntry>();
    public string? Error { get; init; }
}
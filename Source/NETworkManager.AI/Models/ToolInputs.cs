namespace NETworkManager.AI.Models;

/// <summary>Provider-neutral DNS record types.</summary>
public enum DnsRecordType
{
    A, AAAA, CNAME, MX, NS, PTR, SOA, SRV, TXT, ANY, CAA, DNSKEY,
}

/// <summary>IP address family selector for routing-table queries.</summary>
public enum IPAddressFamily
{
    IPv4 = 0,
    IPv6 = 1,
}

/// <summary>Input for <c>ping</c>: ICMP echo to a target host.</summary>
public sealed record PingInput : IValidatableToolInput
{
    public string? Target { get; init; }
    public int Count { get; init; } = 4;
    public int TimeoutMilliseconds { get; init; } = 4000;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Target)) errors.Add("Target must not be empty.");
        if (Count is < 1 or > 100) errors.Add("Count must be between 1 and 100.");
        if (TimeoutMilliseconds is < 100 or > 60000) errors.Add("TimeoutMilliseconds must be between 100 and 60000.");
        return errors;
    }
}

/// <summary>Input for <c>dns_lookup</c>: query DNS records for a host.</summary>
public sealed record DnsLookupInput : IValidatableToolInput
{
    public string? Host { get; init; }
    public DnsRecordType RecordType { get; init; } = DnsRecordType.A;
    public string? Server { get; init; }
    public int TimeoutMilliseconds { get; init; } = 4000;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Host)) errors.Add("Host must not be empty.");
        if (TimeoutMilliseconds is < 100 or > 60000) errors.Add("TimeoutMilliseconds must be between 100 and 60000.");
        return errors;
    }
}

/// <summary>Input for <c>tcp_test</c>: TCP connectivity probe to a host:port.</summary>
public sealed record TcpTestInput : IValidatableToolInput
{
    public string? Host { get; init; }
    public int Port { get; init; }
    public int TimeoutMilliseconds { get; init; } = 4000;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Host)) errors.Add("Host must not be empty.");
        if (Port is < 1 or > 65535) errors.Add("Port must be between 1 and 65535.");
        if (TimeoutMilliseconds is < 100 or > 60000) errors.Add("TimeoutMilliseconds must be between 100 and 60000.");
        return errors;
    }
}

/// <summary>Input for <c>traceroute</c>: discover the path to a target host.</summary>
public sealed record TracerouteInput : IValidatableToolInput
{
    public string? Target { get; init; }
    public int MaximumHops { get; init; } = 30;
    public int TimeoutMilliseconds { get; init; } = 4000;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Target)) errors.Add("Target must not be empty.");
        if (MaximumHops is < 1 or > 64) errors.Add("MaximumHops must be between 1 and 64.");
        if (TimeoutMilliseconds is < 100 or > 60000) errors.Add("TimeoutMilliseconds must be between 100 and 60000.");
        return errors;
    }
}

/// <summary>Input for <c>network_adapter_info</c>: enumerate local network adapters (optional name filter).</summary>
public sealed record NetworkAdapterInput : IValidatableToolInput
{
    public string? NameFilter { get; init; }

    public IReadOnlyList<string> Validate() => Array.Empty<string>();
}

/// <summary>Input for <c>routing_table</c>: read the local IP routing table.</summary>
public sealed record RoutingTableInput : IValidatableToolInput
{
    public IPAddressFamily AddressFamily { get; init; } = IPAddressFamily.IPv4;

    public IReadOnlyList<string> Validate() => Array.Empty<string>();
}
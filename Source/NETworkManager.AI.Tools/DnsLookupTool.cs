using DnsClient;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.Models.Network;

namespace NETworkManager.AI.Tools;

/// <summary>
///     <c>dns_lookup</c> tool. Wraps the NETworkManager <see cref="DNSLookup"/> engine.
///     Note: the upstream engine has no cancellation support; the execution service enforces the outer timeout/cancellation.
/// </summary>
public sealed class DnsLookupTool : INetworkTool
{
    public string Name => "dns_lookup";
    public string Description => "Resolves DNS records for a host name or IP address.";
    public ToolCategory Category => ToolCategory.Resolution;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);
    public Type InputType => typeof(DnsLookupInput);
    public Type OutputType => typeof(DnsLookupResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var dnsInput = (DnsLookupInput)input!;

        IEnumerable<ServerConnectionInfo>? servers = null;
        if (!string.IsNullOrWhiteSpace(dnsInput.Server))
            servers = new[] { new ServerConnectionInfo(dnsInput.Server, 53) };

        var settings = new DNSLookupSettings
        {
            AddDNSSuffix = false,
            QueryType = ToQueryType(dnsInput.RecordType),
            UseCache = false,
            Recursion = true,
            Retries = 1,
            Timeout = TimeSpan.FromMilliseconds(dnsInput.TimeoutMilliseconds),
        };

        var lookup = new DNSLookup(settings, servers);

        var records = new List<DnsRecord>();
        string? error = null;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        lookup.RecordReceived += (_, e) =>
        {
            var r = e.Args;
            lock (records) records.Add(new DnsRecord(r.DomainName, r.TTL, r.RecordType, r.Result));
        };

        lookup.LookupError += (_, e) => error ??= e.ErrorMessage;
        lookup.LookupComplete += (_, _) => completion.TrySetResult();

        lookup.ResolveAsync(new[] { dnsInput.Host! });

        await completion.Task.ConfigureAwait(false);

        var snapshot = records.ToList();
        if (snapshot.Count == 0)
            return ToolOutcome.Failed("DnsLookupFailed", error ?? $"No DNS records returned for '{dnsInput.Host}'.");

        return ToolOutcome.Ok(new DnsLookupResult { Host = dnsInput.Host!, Success = true, Records = snapshot });
    }

    private static QueryType ToQueryType(DnsRecordType recordType) => recordType switch
    {
        DnsRecordType.A => QueryType.A,
        DnsRecordType.AAAA => QueryType.AAAA,
        DnsRecordType.CNAME => QueryType.CNAME,
        DnsRecordType.MX => QueryType.MX,
        DnsRecordType.NS => QueryType.NS,
        DnsRecordType.PTR => QueryType.PTR,
        DnsRecordType.SOA => QueryType.SOA,
        DnsRecordType.SRV => QueryType.SRV,
        DnsRecordType.TXT => QueryType.TXT,
        DnsRecordType.ANY => QueryType.ANY,
        DnsRecordType.CAA => QueryType.CAA,
        DnsRecordType.DNSKEY => QueryType.DNSKEY,
        _ => QueryType.A,
    };
}
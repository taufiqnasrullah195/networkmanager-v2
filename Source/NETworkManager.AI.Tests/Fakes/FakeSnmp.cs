using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Snmp;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>Scripted <see cref="ISnmpProvider"/> returning canned variables (or throwing) with no real device.</summary>
public sealed class FakeSnmpProvider : ISnmpProvider
{
    public Func<string, IReadOnlyList<string>, IReadOnlyList<SnmpVariable>>? OnGet { get; set; }

    public Func<string, IReadOnlyList<SnmpVariable>>? OnWalk { get; set; }

    public Exception? ThrowOnGet { get; set; }

    public Exception? ThrowOnWalk { get; set; }

    public List<string> GetOids { get; } = [];

    public List<string> WalkOids { get; } = [];

    public string? LastHost { get; private set; }

    public Task<IReadOnlyList<SnmpVariable>> GetAsync(SnmpSession session, IReadOnlyList<string> oids,
        CancellationToken cancellationToken = default)
    {
        LastHost = session.Host;
        GetOids.AddRange(oids);

        if (ThrowOnGet is not null)
            return Task.FromException<IReadOnlyList<SnmpVariable>>(ThrowOnGet);

        return Task.FromResult(OnGet?.Invoke(session.Host, oids) ?? Array.Empty<SnmpVariable>());
    }

    public Task<IReadOnlyList<SnmpVariable>> WalkAsync(SnmpSession session, string oid,
        CancellationToken cancellationToken = default)
    {
        LastHost = session.Host;
        WalkOids.Add(oid);

        if (ThrowOnWalk is not null)
            return Task.FromException<IReadOnlyList<SnmpVariable>>(ThrowOnWalk);

        return Task.FromResult(OnWalk?.Invoke(oid) ?? Array.Empty<SnmpVariable>());
    }
}

/// <summary>Scripted <see cref="ISnmpTelemetryCollector"/> for executor tests.</summary>
public sealed class FakeSnmpCollector : ISnmpTelemetryCollector
{
    public SnmpCollectionResult? Result { get; set; }

    public Exception? Throw { get; set; }

    public string? LastTargetId { get; private set; }

    public string? LastHost { get; private set; }

    public SnmpCheckConfig? LastConfig { get; private set; }

    public Task<SnmpCollectionResult> CollectAsync(string targetId, string host, SnmpCheckConfig config,
        CancellationToken cancellationToken = default)
    {
        LastTargetId = targetId;
        LastHost = host;
        LastConfig = config;

        if (Throw is not null)
            return Task.FromException<SnmpCollectionResult>(Throw);

        return Task.FromResult(Result ?? throw new InvalidOperationException("FakeSnmpCollector.Result is not set."));
    }
}

/// <summary>Builders for canned SNMP raw data / telemetry used across the test suite.</summary>
public static class SnmpTestData
{
    public static SnmpVariable Var(string oid, string value) => new(oid, value);

    public static SnmpVariable[] System(string name = "SW01", string desc = "Cisco IOS", string oid = "1.3.6.1.4.1.9.1.1000",
        ulong uptimeHundredths = 1_000_000) =>
    [
        Var(SnmpNormalizer.SysName, name),
        Var(SnmpNormalizer.SysDescr, desc),
        Var(SnmpNormalizer.SysObjectId, oid),
        Var(SnmpNormalizer.SysUpTime, uptimeHundredths.ToString()),
    ];

    public static SnmpVariable[] Interfaces(params (int Index, string Oper, string InOctets, string OutOctets)[] rows) =>
        rows.SelectMany(r => new[]
        {
            Var($"{SnmpNormalizer.IfDescr}.{r.Index}", $"Gi1/0/{r.Index}"),
            Var($"{SnmpNormalizer.IfAdminStatus}.{r.Index}", "1"),
            Var($"{SnmpNormalizer.IfOperStatus}.{r.Index}", r.Oper),
            Var($"{SnmpNormalizer.IfSpeed}.{r.Index}", "1000000000"),
            Var($"{SnmpNormalizer.IfInOctets}.{r.Index}", r.InOctets),
            Var($"{SnmpNormalizer.IfOutOctets}.{r.Index}", r.OutOctets),
            Var($"{SnmpNormalizer.IfInErrors}.{r.Index}", "0"),
            Var($"{SnmpNormalizer.IfOutErrors}.{r.Index}", "1"),
        }).ToArray();

    public static SnmpVariable[] IfX(params (int Index, string InOctets, string OutOctets)[] rows) =>
        rows.SelectMany(r => new[]
        {
            Var($"{SnmpNormalizer.IfName}.{r.Index}", $"Gi1/0/{r.Index}"),
            Var($"{SnmpNormalizer.IfHCInOctets}.{r.Index}", r.InOctets),
            Var($"{SnmpNormalizer.IfHCOutOctets}.{r.Index}", r.OutOctets),
        }).ToArray();
}

/// <summary>In-memory <see cref="ISnmpTelemetryRepository"/> for tool tests.</summary>
public sealed class FakeSnmpTelemetryRepository : ISnmpTelemetryRepository
{
    public DeviceTelemetry? LatestDevice { get; set; }

    public IReadOnlyList<InterfaceTelemetry> LatestInterfaces { get; set; } = Array.Empty<InterfaceTelemetry>();

    public IReadOnlyList<InterfaceTelemetry> AllLatestInterfaces { get; set; } = Array.Empty<InterfaceTelemetry>();

    public IReadOnlyList<InterfaceTelemetry> InterfaceHistory { get; set; } = Array.Empty<InterfaceTelemetry>();

    public List<SnmpCollectionResult> Recorded { get; } = [];

    public Task RecordAsync(SnmpCollectionResult collection, CancellationToken cancellationToken = default)
    {
        Recorded.Add(collection);
        return Task.CompletedTask;
    }

    public Task<DeviceTelemetry?> GetLatestDeviceTelemetryAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(LatestDevice);

    public Task<IReadOnlyList<InterfaceTelemetry>> GetLatestInterfaceTelemetryAsync(string deviceId, int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LatestInterfaces);

    public Task<IReadOnlyList<InterfaceTelemetry>> GetAllLatestInterfaceTelemetryAsync(int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InterfaceTelemetry>>(AllLatestInterfaces.Take(limit).ToList());

    public Task<IReadOnlyList<InterfaceTelemetry>> GetInterfaceTelemetryHistoryAsync(string deviceId, int interfaceIndex,
        DateTimeOffset? start, DateTimeOffset? end, int limit, int offset, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InterfaceTelemetry>>(InterfaceHistory.Skip(offset).Take(limit).ToList());
}

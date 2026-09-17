using System.Globalization;

namespace NETworkManager.AI.Snmp;

/// <summary>
///     Standard MIB-II / IF-MIB OIDs and the normalization from raw SNMP variables into structured telemetry.
///     Missing values are <c>null</c> ("unavailable"), never fabricated. Standard vs vendor-specific OIDs are kept
///     strictly separate — only the standard MIB-II and IF-MIB OIDs are implemented here.
/// </summary>
public sealed class SnmpNormalizer : ISnmpNormalizer
{
    // MIB-II system group (standard).
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";
    public const string SysObjectId = "1.3.6.1.2.1.1.2.0";
    public const string SysUpTime = "1.3.6.1.2.1.1.3.0";
    public const string SysName = "1.3.6.1.2.1.1.5.0";

    // IF-MIB ifTable (standard).
    public const string IfTable = "1.3.6.1.2.1.2.2.1";
    public const string IfIndex = "1.3.6.1.2.1.2.2.1.1";
    public const string IfDescr = "1.3.6.1.2.1.2.2.1.2";
    public const string IfSpeed = "1.3.6.1.2.1.2.2.1.5";
    public const string IfAdminStatus = "1.3.6.1.2.1.2.2.1.7";
    public const string IfOperStatus = "1.3.6.1.2.1.2.2.1.8";
    public const string IfInOctets = "1.3.6.1.2.1.2.2.1.10";
    public const string IfInDiscards = "1.3.6.1.2.1.2.2.1.13";
    public const string IfInErrors = "1.3.6.1.2.1.2.2.1.14";
    public const string IfOutOctets = "1.3.6.1.2.1.2.2.1.16";
    public const string IfOutDiscards = "1.3.6.1.2.1.2.2.1.19";
    public const string IfOutErrors = "1.3.6.1.2.1.2.2.1.20";

    // IF-MIB ifXTable (standard, high-capacity 64-bit counters).
    public const string IfXTable = "1.3.6.1.2.1.31.1.1.1";
    public const string IfName = "1.3.6.1.2.1.31.1.1.1.1";
    public const string IfHCInOctets = "1.3.6.1.2.1.31.1.1.1.6";
    public const string IfHCOutOctets = "1.3.6.1.2.1.31.1.1.1.10";

    public DeviceTelemetry NormalizeSystem(string deviceId, string target, DateTimeOffset timestamp,
        IReadOnlyList<SnmpVariable> variables, bool reachable, TimeSpan responseTime)
    {
        var lookup = ToLookup(variables);

        return new DeviceTelemetry
        {
            DeviceId = deviceId,
            Target = target,
            Timestamp = timestamp,
            SysName = lookup.GetValueOrDefault(SysName),
            SysDescription = lookup.GetValueOrDefault(SysDescr),
            SysObjectId = lookup.GetValueOrDefault(SysObjectId),
            Uptime = ParseUptime(lookup.GetValueOrDefault(SysUpTime)),
            Reachable = reachable,
            ResponseTime = responseTime,
        };
    }

    public IReadOnlyList<InterfaceTelemetry> NormalizeInterfaces(string deviceId, DateTimeOffset timestamp,
        IReadOnlyList<SnmpVariable> variables)
    {
        // Group by interface index: OID = <tablePrefix>.<column>.<index>. We split off the last segment as the index
        // and the segment before it as the column.
        var rows = new SortedDictionary<int, Dictionary<string, string>>();

        foreach (var variable in variables)
        {
            var (column, index) = SplitColumnIndex(variable.Oid);
            if (index < 0)
                continue;

            if (!rows.TryGetValue(index, out var row))
            {
                row = new Dictionary<string, string>();
                rows[index] = row;
            }

            row[column] = variable.Value;
        }

        var interfaces = new List<InterfaceTelemetry>(rows.Count);

        foreach (var (index, row) in rows)
        {
            var hcIn = ParseUlong(row.GetValueOrDefault(IfHCInOctets));
            var hcOut = ParseUlong(row.GetValueOrDefault(IfHCOutOctets));
            var usesHc = hcIn is not null || hcOut is not null;

            interfaces.Add(new InterfaceTelemetry
            {
                DeviceId = deviceId,
                InterfaceIndex = index,
                Name = row.GetValueOrDefault(IfName) ?? row.GetValueOrDefault(IfDescr),
                Description = row.GetValueOrDefault(IfDescr),
                AdminStatus = ParseAdminStatus(row.GetValueOrDefault(IfAdminStatus)),
                OperationalStatus = ParseOperStatus(row.GetValueOrDefault(IfOperStatus)),
                SpeedBitsPerSecond = ParseUlong(row.GetValueOrDefault(IfSpeed)),
                InOctets = usesHc ? hcIn ?? ParseUlong(row.GetValueOrDefault(IfInOctets)) : ParseUlong(row.GetValueOrDefault(IfInOctets)),
                OutOctets = usesHc ? hcOut ?? ParseUlong(row.GetValueOrDefault(IfOutOctets)) : ParseUlong(row.GetValueOrDefault(IfOutOctets)),
                InErrors = ParseUlong(row.GetValueOrDefault(IfInErrors)),
                OutErrors = ParseUlong(row.GetValueOrDefault(IfOutErrors)),
                InDiscards = ParseUlong(row.GetValueOrDefault(IfInDiscards)),
                OutDiscards = ParseUlong(row.GetValueOrDefault(IfOutDiscards)),
                UsesHighCapacityCounters = usesHc,
                Timestamp = timestamp,
            });
        }

        return interfaces;
    }

    private static Dictionary<string, string> ToLookup(IReadOnlyList<SnmpVariable> variables)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var variable in variables)
            lookup[variable.Oid] = variable.Value;

        return lookup;
    }

    /// <summary>Splits "&lt;column-oid&gt;.&lt;index&gt;" into (column-oid, index). Returns index -1 when the suffix cannot be parsed.</summary>
    private static (string Column, int Index) SplitColumnIndex(string oid)
    {
        var lastDot = oid.LastIndexOf('.');
        if (lastDot <= 0)
            return (oid, -1);

        var column = oid[..lastDot];
        if (!int.TryParse(oid[(lastDot + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index))
            return (column, -1);

        return (column, index);
    }

    private static TimeSpan? ParseUptime(string? value)
    {
        // sysUpTime is TimeTicks: hundredths of a second.
        if (value is null || !ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            return null;

        return TimeSpan.FromMilliseconds(ticks * 10);
    }

    private static ulong? ParseUlong(string? value) =>
        value is not null && ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static SnmpInterfaceAdminStatus ParseAdminStatus(string? value) =>
        int.TryParse(value, out var parsed) && Enum.IsDefined(typeof(SnmpInterfaceAdminStatus), parsed)
            ? (SnmpInterfaceAdminStatus)parsed
            : SnmpInterfaceAdminStatus.Unknown;

    private static SnmpInterfaceOperStatus ParseOperStatus(string? value) =>
        int.TryParse(value, out var parsed) && Enum.IsDefined(typeof(SnmpInterfaceOperStatus), parsed)
            ? (SnmpInterfaceOperStatus)parsed
            : SnmpInterfaceOperStatus.Unknown;
}

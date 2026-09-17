namespace NETworkManager.AI.Snmp;

/// <summary>Maps raw SNMP variables (MIB-II system group + IF-MIB interface tables) into normalized telemetry models.</summary>
public interface ISnmpNormalizer
{
    /// <summary>Normalizes the four MIB-II system group OIDs into a <see cref="DeviceTelemetry"/> (missing → <c>null</c> fields).</summary>
    DeviceTelemetry NormalizeSystem(string deviceId, string target, DateTimeOffset timestamp,
        IReadOnlyList<SnmpVariable> variables, bool reachable, TimeSpan responseTime);

    /// <summary>Normalizes IF-MIB ifTable/ifXTable variables into interface telemetry, preferring 64-bit counters.</summary>
    IReadOnlyList<InterfaceTelemetry> NormalizeInterfaces(string deviceId, DateTimeOffset timestamp,
        IReadOnlyList<SnmpVariable> variables);
}

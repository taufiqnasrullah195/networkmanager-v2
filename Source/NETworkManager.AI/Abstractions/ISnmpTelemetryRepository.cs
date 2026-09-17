using NETworkManager.AI.Snmp;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Persists and reads SNMP telemetry history (device snapshots + interface counters). Read-only from the caller's
///     perspective; the only mutation is recording a completed collection.
/// </summary>
public interface ISnmpTelemetryRepository
{
    /// <summary>Records a completed collection (best-effort — persistence failure must not fail the check).</summary>
    Task RecordAsync(SnmpCollectionResult collection, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent device telemetry snapshot for a device, or <c>null</c> if none.</summary>
    Task<DeviceTelemetry?> GetLatestDeviceTelemetryAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns the latest interface telemetry rows (one per interface index) for a device.</summary>
    Task<IReadOnlyList<InterfaceTelemetry>> GetLatestInterfaceTelemetryAsync(string deviceId, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the latest interface telemetry rows across ALL devices (one per device+interface index).</summary>
    Task<IReadOnlyList<InterfaceTelemetry>> GetAllLatestInterfaceTelemetryAsync(int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Bounded interface counter history (for rate calculation and trends).</summary>
    Task<IReadOnlyList<InterfaceTelemetry>> GetInterfaceTelemetryHistoryAsync(string deviceId, int interfaceIndex,
        DateTimeOffset? start, DateTimeOffset? end, int limit, int offset, CancellationToken cancellationToken = default);
}

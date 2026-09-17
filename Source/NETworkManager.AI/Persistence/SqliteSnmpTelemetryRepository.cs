using System.Globalization;
using Microsoft.Data.Sqlite;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Snmp;

namespace NETworkManager.AI.Persistence;

/// <summary>SQLite-backed SNMP telemetry history. Counter values are stored as exact decimal TEXT (Counter64 can exceed INTEGER).</summary>
public sealed class SqliteSnmpTelemetryRepository : ISnmpTelemetryRepository
{
    private readonly SqliteDatabase _db;

    public SqliteSnmpTelemetryRepository(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task RecordAsync(SnmpCollectionResult collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var transaction = connection.BeginTransaction();

        if (collection.Device is { } device)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO snmp_device_telemetry
                    (device_id, target, timestamp_ms, sys_name, sys_description, sys_object_id, uptime_ms,
                     interface_count, reachable, collection_status, response_ms)
                VALUES
                    ($deviceId, $target, $ts, $sysName, $sysDesc, $sysOid, $uptimeMs,
                     $ifCount, $reachable, $status, $responseMs);
                """;
            command.Parameters.AddWithValue("$deviceId", device.DeviceId);
            command.Parameters.AddWithValue("$target", device.Target);
            command.Parameters.AddWithValue("$ts", PersistenceClock.ToUtcMillis(device.Timestamp));
            command.Parameters.AddWithValue("$sysName", (object?)device.SysName ?? DBNull.Value);
            command.Parameters.AddWithValue("$sysDesc", (object?)device.SysDescription ?? DBNull.Value);
            command.Parameters.AddWithValue("$sysOid", (object?)device.SysObjectId ?? DBNull.Value);
            command.Parameters.AddWithValue("$uptimeMs", device.Uptime is { } uptime ? (long)uptime.TotalMilliseconds : DBNull.Value);
            command.Parameters.AddWithValue("$ifCount", device.InterfaceCount);
            command.Parameters.AddWithValue("$reachable", device.Reachable ? 1 : 0);
            command.Parameters.AddWithValue("$status", (int)device.CollectionStatus);
            command.Parameters.AddWithValue("$responseMs", (long)device.ResponseTime.TotalMilliseconds);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var iface in collection.Interfaces)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO snmp_interface_telemetry
                    (device_id, interface_index, interface_name, admin_status, oper_status, speed,
                     in_octets, out_octets, in_errors, out_errors, in_discards, out_discards, uses_hc, timestamp_ms)
                VALUES
                    ($deviceId, $index, $name, $admin, $oper, $speed,
                     $inOctets, $outOctets, $inErrors, $outErrors, $inDiscards, $outDiscards, $usesHc, $ts);
                """;
            command.Parameters.AddWithValue("$deviceId", iface.DeviceId);
            command.Parameters.AddWithValue("$index", iface.InterfaceIndex);
            command.Parameters.AddWithValue("$name", (object?)iface.Name ?? DBNull.Value);
            command.Parameters.AddWithValue("$admin", (int)iface.AdminStatus);
            command.Parameters.AddWithValue("$oper", (int)iface.OperationalStatus);
            command.Parameters.AddWithValue("$speed", iface.SpeedBitsPerSecond is { } speed ? (object)(long)speed : DBNull.Value);
            command.Parameters.AddWithValue("$inOctets", ToDb(iface.InOctets));
            command.Parameters.AddWithValue("$outOctets", ToDb(iface.OutOctets));
            command.Parameters.AddWithValue("$inErrors", ToDb(iface.InErrors));
            command.Parameters.AddWithValue("$outErrors", ToDb(iface.OutErrors));
            command.Parameters.AddWithValue("$inDiscards", ToDb(iface.InDiscards));
            command.Parameters.AddWithValue("$outDiscards", ToDb(iface.OutDiscards));
            command.Parameters.AddWithValue("$usesHc", iface.UsesHighCapacityCounters ? 1 : 0);
            command.Parameters.AddWithValue("$ts", PersistenceClock.ToUtcMillis(iface.Timestamp));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DeviceTelemetry?> GetLatestDeviceTelemetryAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT device_id, target, timestamp_ms, sys_name, sys_description, sys_object_id, uptime_ms,
                   interface_count, reachable, collection_status, response_ms
            FROM snmp_device_telemetry WHERE device_id = $deviceId ORDER BY timestamp_ms DESC, id DESC LIMIT 1;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return ReadDevice(reader);
    }

    public async Task<IReadOnlyList<InterfaceTelemetry>> GetLatestInterfaceTelemetryAsync(string deviceId, int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT device_id, interface_index, interface_name, admin_status, oper_status, speed,
                   in_octets, out_octets, in_errors, out_errors, in_discards, out_discards, uses_hc, timestamp_ms
            FROM snmp_interface_telemetry
            WHERE id IN (SELECT MAX(id) FROM snmp_interface_telemetry WHERE device_id = $deviceId GROUP BY interface_index)
            ORDER BY interface_index LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<InterfaceTelemetry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadInterface(reader));

        return results;
    }

    public async Task<IReadOnlyList<InterfaceTelemetry>> GetInterfaceTelemetryHistoryAsync(string deviceId,
        int interfaceIndex, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string> { "device_id = $deviceId", "interface_index = $index" };
        var parameters = new List<(string, object?)> { ("$deviceId", deviceId), ("$index", interfaceIndex) };

        if (start is not null)
        {
            conditions.Add("timestamp_ms >= $start");
            parameters.Add(("$start", PersistenceClock.ToUtcMillis(start.Value)));
        }

        if (end is not null)
        {
            conditions.Add("timestamp_ms <= $end");
            parameters.Add(("$end", PersistenceClock.ToUtcMillis(end.Value)));
        }

        parameters.Add(("$limit", limit));
        parameters.Add(("$offset", offset));

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT device_id, interface_index, interface_name, admin_status, oper_status, speed,
                   in_octets, out_octets, in_errors, out_errors, in_discards, out_discards, uses_hc, timestamp_ms
            FROM snmp_interface_telemetry
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY timestamp_ms DESC LIMIT $limit OFFSET $offset;
            """;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var results = new List<InterfaceTelemetry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            results.Add(ReadInterface(reader));

        return results;
    }

    private static object ToDb(ulong? value) => value is { } v ? (object)v.ToString(CultureInfo.InvariantCulture) : DBNull.Value;

    private static ulong? FromDb(object value) =>
        value is DBNull || value is null
            ? null
            : ulong.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;

    private static DeviceTelemetry ReadDevice(SqliteDataReader reader) => new()
    {
        DeviceId = reader.GetString(0),
        Target = reader.GetString(1),
        Timestamp = PersistenceClock.FromUtcMillis(reader.GetInt64(2)),
        SysName = reader.IsDBNull(3) ? null : reader.GetString(3),
        SysDescription = reader.IsDBNull(4) ? null : reader.GetString(4),
        SysObjectId = reader.IsDBNull(5) ? null : reader.GetString(5),
        Uptime = reader.IsDBNull(6) ? null : TimeSpan.FromMilliseconds(reader.GetInt64(6)),
        InterfaceCount = reader.GetInt32(7),
        Reachable = reader.GetInt32(8) != 0,
        CollectionStatus = (SnmpCollectionStatus)reader.GetInt32(9),
        ResponseTime = TimeSpan.FromMilliseconds(reader.GetInt64(10)),
    };

    private static InterfaceTelemetry ReadInterface(SqliteDataReader reader) => new()
    {
        DeviceId = reader.GetString(0),
        InterfaceIndex = reader.GetInt32(1),
        Name = reader.IsDBNull(2) ? null : reader.GetString(2),
        AdminStatus = (SnmpInterfaceAdminStatus)reader.GetInt32(3),
        OperationalStatus = (SnmpInterfaceOperStatus)reader.GetInt32(4),
        SpeedBitsPerSecond = reader.IsDBNull(5) ? null : (ulong)reader.GetInt64(5),
        InOctets = FromDb(reader.GetValue(6)),
        OutOctets = FromDb(reader.GetValue(7)),
        InErrors = FromDb(reader.GetValue(8)),
        OutErrors = FromDb(reader.GetValue(9)),
        InDiscards = FromDb(reader.GetValue(10)),
        OutDiscards = FromDb(reader.GetValue(11)),
        UsesHighCapacityCounters = reader.GetInt32(12) != 0,
        Timestamp = PersistenceClock.FromUtcMillis(reader.GetInt64(13)),
    };
}

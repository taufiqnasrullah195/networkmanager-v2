using Microsoft.Data.Sqlite;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Snmp;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SnmpPersistenceTests
{
    [Fact]
    public async Task Fresh_database_is_schema_version_2()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        using var connection = new SqliteConnection($"Data Source={db.Database.Path}");
        connection.Open();

        Assert.Equal(2L, Scalar(connection, "PRAGMA user_version;"));
        Assert.NotNull(Scalar(connection, "SELECT name FROM sqlite_master WHERE name = 'snmp_device_telemetry';"));
        Assert.NotNull(Scalar(connection, "SELECT name FROM sqlite_master WHERE name = 'snmp_interface_telemetry';"));
    }

    [Fact]
    public async Task Migrates_v1_database_to_v2()
    {
        using var db = new SqliteTestDb();

        using (var connection = new SqliteConnection($"Data Source={db.Database.Path}"))
        {
            connection.Open();
            Execute(connection, "CREATE TABLE monitoring_results (id INTEGER PRIMARY KEY AUTOINCREMENT, target_id TEXT NOT NULL);");
            Execute(connection, "PRAGMA user_version = 1;");
        }

        await db.InitializeAsync();

        using var connection2 = new SqliteConnection($"Data Source={db.Database.Path}");
        connection2.Open();
        Assert.Equal(2L, Scalar(connection2, "PRAGMA user_version;"));
        Assert.NotNull(Scalar(connection2, "SELECT name FROM sqlite_master WHERE name = 'snmp_interface_telemetry';"));
    }

    [Fact]
    public async Task Repository_records_and_reads_latest_telemetry()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        var repository = new SqliteSnmpTelemetryRepository(db.Database);

        await repository.RecordAsync(Collection("sw01", inOctets: 1000, outOctets: 2000));

        var device = await repository.GetLatestDeviceTelemetryAsync("sw01");
        Assert.NotNull(device);
        Assert.Equal("SW01", device!.SysName);
        Assert.Equal(TimeSpan.FromDays(14), device.Uptime);

        var interfaces = await repository.GetLatestInterfaceTelemetryAsync("sw01", 100);
        Assert.Single(interfaces);
        Assert.Equal(1000ul, interfaces[0].InOctets);
    }

    [Fact]
    public async Task Latest_interface_query_returns_the_most_recent_sample_per_interface()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        var repository = new SqliteSnmpTelemetryRepository(db.Database);

        await repository.RecordAsync(Collection("sw01", inOctets: 1000, outOctets: 2000, timestamp: DateTimeOffset.UtcNow.AddMinutes(-2)));
        await repository.RecordAsync(Collection("sw01", inOctets: 5000, outOctets: 9000, timestamp: DateTimeOffset.UtcNow));

        var interfaces = await repository.GetLatestInterfaceTelemetryAsync("sw01", 100);

        Assert.Single(interfaces);
        Assert.Equal(5000ul, interfaces[0].InOctets);
        Assert.Equal(9000ul, interfaces[0].OutOctets);
    }

    [Fact]
    public async Task Interface_history_is_bounded_and_ordered_descending()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        var repository = new SqliteSnmpTelemetryRepository(db.Database);

        await repository.RecordAsync(Collection("sw01", inOctets: 1000, timestamp: DateTimeOffset.UtcNow.AddMinutes(-2)));
        await repository.RecordAsync(Collection("sw01", inOctets: 5000, timestamp: DateTimeOffset.UtcNow));

        var history = await repository.GetInterfaceTelemetryHistoryAsync("sw01", 1, null, null, 10, 0);

        Assert.Equal(2, history.Count);
        Assert.True(history[0].Timestamp >= history[1].Timestamp);
    }

    [Fact]
    public async Task Large_counter_values_survive_round_trip()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        var repository = new SqliteSnmpTelemetryRepository(db.Database);

        const ulong big = 9_000_000_000_000_000_000; // exceeds signed 64-bit INTEGER
        await repository.RecordAsync(Collection("sw01", inOctets: big));

        var interfaces = await repository.GetLatestInterfaceTelemetryAsync("sw01", 100);
        Assert.Equal(big, interfaces[0].InOctets);
    }

    [Fact]
    public async Task Retention_deletes_expired_snmp_rows_only()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();
        var repository = new SqliteSnmpTelemetryRepository(db.Database);

        var now = DateTimeOffset.UtcNow;
        await repository.RecordAsync(Collection("old", timestamp: now.AddDays(-100)));    // device > 90d, interface > 30d
        await repository.RecordAsync(Collection("recent", timestamp: now));              // within windows

        var retention = new SqliteRetentionService(db.Database);
        var deleted = await retention.RunCleanupAsync(new RetentionPolicy());

        Assert.True(deleted >= 2); // at least the expired device + interface rows
        Assert.Null(await repository.GetLatestDeviceTelemetryAsync("old"));
        Assert.NotNull(await repository.GetLatestDeviceTelemetryAsync("recent"));
        Assert.Empty(await repository.GetLatestInterfaceTelemetryAsync("old", 100));
        Assert.Single(await repository.GetLatestInterfaceTelemetryAsync("recent", 100));
    }

    private static SnmpCollectionResult Collection(string deviceId, ulong inOctets = 1000, ulong outOctets = 2000,
        DateTimeOffset? timestamp = null)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow;
        return new SnmpCollectionResult
        {
            TargetId = deviceId,
            Host = "10.0.0.1",
            Timestamp = t,
            Status = SnmpCollectionStatus.Success,
            Device = new DeviceTelemetry
            {
                DeviceId = deviceId,
                Target = "10.0.0.1",
                Timestamp = t,
                SysName = "SW01",
                SysDescription = "Cisco IOS",
                SysObjectId = "1.3.6.1.4.1.9.1.1000",
                Uptime = TimeSpan.FromDays(14),
                InterfaceCount = 1,
                Reachable = true,
                CollectionStatus = SnmpCollectionStatus.Success,
                ResponseTime = TimeSpan.FromMilliseconds(5),
            },
            Interfaces = new[]
            {
                new InterfaceTelemetry
                {
                    DeviceId = deviceId,
                    InterfaceIndex = 1,
                    Name = "Gi1/0/1",
                    AdminStatus = SnmpInterfaceAdminStatus.Up,
                    OperationalStatus = SnmpInterfaceOperStatus.Up,
                    SpeedBitsPerSecond = 1_000_000_000,
                    InOctets = inOctets,
                    OutOctets = outOctets,
                    InErrors = 0,
                    OutErrors = 1,
                    UsesHighCapacityCounters = true,
                    Timestamp = t,
                },
            },
        };
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

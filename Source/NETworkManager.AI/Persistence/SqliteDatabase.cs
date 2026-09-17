using Microsoft.Data.Sqlite;

namespace NETworkManager.AI.Persistence;

/// <summary>UTC/time helpers — timestamps are stored as Unix-epoch milliseconds (UTC, INTEGER) for indexable range queries.</summary>
public static class PersistenceClock
{
    public static long ToUtcMillis(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    public static DateTimeOffset FromUtcMillis(long millis) => DateTimeOffset.FromUnixTimeMilliseconds(millis);
}

/// <summary>
///     SQLite core: connection management, schema creation, and versioned migrations. Provider-neutral in the sense
///     that the rest of the persistence layer talks to this single class; a different storage backend would replace it.
/// </summary>
public sealed class SqliteDatabase
{
    public const int CurrentSchemaVersion = 2;

    private const string SchemaV1 = """
        CREATE TABLE IF NOT EXISTS monitoring_results (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            target_id TEXT NOT NULL,
            check_id TEXT NOT NULL,
            check_type INTEGER NOT NULL,
            status INTEGER NOT NULL,
            error_classification INTEGER NOT NULL,
            timestamp_ms INTEGER NOT NULL,
            duration_ms INTEGER NOT NULL,
            safe_message TEXT,
            observed_json TEXT,
            correlation_id TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_results_target_time ON monitoring_results(target_id, timestamp_ms);
        CREATE INDEX IF NOT EXISTS ix_results_check_time ON monitoring_results(check_id, timestamp_ms);

        CREATE TABLE IF NOT EXISTS state_transitions (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            target_id TEXT NOT NULL,
            timestamp_ms INTEGER NOT NULL,
            previous_state INTEGER NOT NULL,
            new_state INTEGER NOT NULL,
            reason TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_transitions_target_time ON state_transitions(target_id, timestamp_ms);

        CREATE TABLE IF NOT EXISTS alerts (
            alert_id TEXT PRIMARY KEY,
            fingerprint TEXT NOT NULL,
            target_id TEXT NOT NULL,
            target_name TEXT,
            profile_id TEXT,
            severity INTEGER NOT NULL,
            status INTEGER NOT NULL,
            title TEXT NOT NULL,
            description TEXT,
            reason TEXT,
            evidence TEXT,
            first_seen_ms INTEGER NOT NULL,
            last_seen_ms INTEGER NOT NULL,
            occurrence_count INTEGER NOT NULL,
            previous_health INTEGER NOT NULL,
            current_health INTEGER NOT NULL,
            failure_classification TEXT,
            acknowledged_at_ms INTEGER,
            resolved_at_ms INTEGER,
            resolution_evidence TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_alerts_status_time ON alerts(status, last_seen_ms);
        CREATE INDEX IF NOT EXISTS ix_alerts_target_time ON alerts(target_id, last_seen_ms);

        CREATE TABLE IF NOT EXISTS alert_occurrences (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            alert_id TEXT NOT NULL,
            timestamp_ms INTEGER NOT NULL,
            reason TEXT
        );
        CREATE INDEX IF NOT EXISTS ix_occurrences_alert_time ON alert_occurrences(alert_id, timestamp_ms);
        """;

    // SNMP telemetry history (Step 13). Counters are stored as TEXT (exact decimal) because Counter64 can exceed
    // SQLite's signed 64-bit INTEGER range; speed is a Gauge32 (fits INTEGER). No secrets anywhere.
    private const string SchemaV2 = """
        CREATE TABLE IF NOT EXISTS snmp_device_telemetry (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            device_id TEXT NOT NULL,
            target TEXT NOT NULL,
            timestamp_ms INTEGER NOT NULL,
            sys_name TEXT,
            sys_description TEXT,
            sys_object_id TEXT,
            uptime_ms INTEGER,
            interface_count INTEGER NOT NULL,
            reachable INTEGER NOT NULL,
            collection_status INTEGER NOT NULL,
            response_ms INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_snmp_device_time ON snmp_device_telemetry(device_id, timestamp_ms);

        CREATE TABLE IF NOT EXISTS snmp_interface_telemetry (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            device_id TEXT NOT NULL,
            interface_index INTEGER NOT NULL,
            interface_name TEXT,
            admin_status INTEGER NOT NULL,
            oper_status INTEGER NOT NULL,
            speed INTEGER,
            in_octets TEXT,
            out_octets TEXT,
            in_errors TEXT,
            out_errors TEXT,
            in_discards TEXT,
            out_discards TEXT,
            uses_hc INTEGER NOT NULL,
            timestamp_ms INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_snmp_interface_time ON snmp_interface_telemetry(device_id, interface_index, timestamp_ms);
        """;

    private readonly string _connectionString;
    private readonly string _path;

    public SqliteDatabase(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be empty.", nameof(path));

        _path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
    }

    public string Path => _path;

    public SqliteConnection Open() => new(_connectionString);

    /// <summary>Creates/validates the schema and applies migrations. Throws on fatal initialization errors.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, "PRAGMA synchronous=NORMAL;");

        var version = ReadUserVersion(connection);

        if (version == 0)
        {
            Execute(connection, SchemaV1);
            Execute(connection, SchemaV2);
            WriteUserVersion(connection, CurrentSchemaVersion);
        }
        else if (version < CurrentSchemaVersion)
        {
            ApplyMigrations(connection, version);
        }
        else if (version > CurrentSchemaVersion)
        {
            // A future version wrote this database — do not modify it; let callers surface the mismatch.
            throw new InvalidOperationException(
                $"Monitoring database schema version {version} is newer than the supported version {CurrentSchemaVersion}.");
        }
    }

    private static void ApplyMigrations(SqliteConnection connection, int fromVersion)
    {
        // v1 → v2: add the SNMP telemetry history tables.
        if (fromVersion < 2)
            Execute(connection, SchemaV2);

        WriteUserVersion(connection, CurrentSchemaVersion);
    }

    private static int ReadUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var result = command.ExecuteScalar();
        return result is null ? 0 : Convert.ToInt32(result);
    }

    private static void WriteUserVersion(SqliteConnection connection, int version)
    {
        using var command = connection.CreateCommand();
        // PRAGMA does not support bound parameters; the value is an int literal, never user input.
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    /// <summary>Executes a non-query statement with optional parameters (parameterized — never string-concatenated).</summary>
    public static void Execute(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        command.ExecuteNonQuery();
    }
}
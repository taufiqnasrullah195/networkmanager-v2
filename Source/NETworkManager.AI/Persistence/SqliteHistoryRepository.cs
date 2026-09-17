using Microsoft.Data.Sqlite;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Persistence;

/// <summary>Bounded, indexed, parameterized read queries over persisted monitoring/alert history.</summary>
public sealed class SqliteHistoryRepository : IMonitoringHistoryRepository, IAlertHistoryRepository
{
    private readonly SqliteDatabase _db;

    public SqliteHistoryRepository(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<MonitoringHistoryEntry>> GetResultsAsync(
        string? targetId, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var (where, parameters) = BuildFilter("target_id", targetId, start, end);
        parameters.Add(("$limit", limit));
        parameters.Add(("$offset", offset));

        var sql =
            $"""
            SELECT id, target_id, check_id, check_type, status, error_classification, timestamp_ms, duration_ms, safe_message, observed_json, correlation_id
            FROM monitoring_results {where} ORDER BY timestamp_ms DESC LIMIT $limit OFFSET $offset;
            """;

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var results = new List<MonitoringHistoryEntry>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new MonitoringHistoryEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                (MonitorCheckType)reader.GetInt32(3),
                (MonitoringResultStatus)reader.GetInt32(4),
                (MonitorErrorClass)reader.GetInt32(5),
                PersistenceClock.FromUtcMillis(reader.GetInt64(6)),
                TimeSpan.FromMilliseconds(reader.GetInt64(7)),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return results;
    }

    public async Task<IReadOnlyList<StateTransitionEntry>> GetTransitionsAsync(
        string? targetId, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var (where, parameters) = BuildFilter("target_id", targetId, start, end);
        parameters.Add(("$limit", limit));
        parameters.Add(("$offset", offset));

        var sql =
            $"""
            SELECT id, target_id, timestamp_ms, previous_state, new_state, reason
            FROM state_transitions {where} ORDER BY timestamp_ms DESC LIMIT $limit OFFSET $offset;
            """;

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var results = new List<StateTransitionEntry>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new StateTransitionEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                PersistenceClock.FromUtcMillis(reader.GetInt64(2)),
                (NetworkHealthStatus)reader.GetInt32(3),
                (NetworkHealthStatus)reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return results;
    }

    public async Task<IReadOnlyList<AlertHistoryEntry>> GetAlertHistoryAsync(
        string? targetId, DateTimeOffset? start, DateTimeOffset? end, int limit, int offset,
        CancellationToken cancellationToken = default)
    {
        var (where, parameters) = BuildFilter("target_id", targetId, start, end);
        parameters.Add(("$limit", limit));
        parameters.Add(("$offset", offset));

        var sql =
            $"""
            SELECT alert_id, fingerprint, target_id, target_name, profile_id, severity, status, title, description,
                   reason, evidence, first_seen_ms, last_seen_ms, occurrence_count, previous_health, current_health,
                   failure_classification, acknowledged_at_ms, resolved_at_ms, resolution_evidence
            FROM alerts {where} ORDER BY last_seen_ms DESC LIMIT $limit OFFSET $offset;
            """;

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        var results = new List<AlertHistoryEntry>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(ReadAlertHistory(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<AlertOccurrenceEntry>> GetOccurrencesAsync(
        string alertId, int limit, int offset, CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, alert_id, timestamp_ms, reason
            FROM alert_occurrences WHERE alert_id = $alertId ORDER BY timestamp_ms DESC LIMIT $limit OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$alertId", alertId);
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);

        var results = new List<AlertOccurrenceEntry>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new AlertOccurrenceEntry(
                reader.GetInt64(0),
                reader.GetString(1),
                PersistenceClock.FromUtcMillis(reader.GetInt64(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return results;
    }

    private static AlertHistoryEntry ReadAlertHistory(SqliteDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        (AlertSeverity)reader.GetInt32(5),
        (AlertStatus)reader.GetInt32(6),
        reader.GetString(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetString(10),
        PersistenceClock.FromUtcMillis(reader.GetInt64(11)),
        PersistenceClock.FromUtcMillis(reader.GetInt64(12)),
        reader.GetInt32(13),
        (NetworkHealthStatus)reader.GetInt32(14),
        (NetworkHealthStatus)reader.GetInt32(15),
        reader.IsDBNull(16) ? null : reader.GetString(16),
        reader.IsDBNull(17) ? null : PersistenceClock.FromUtcMillis(reader.GetInt64(17)),
        reader.IsDBNull(18) ? null : PersistenceClock.FromUtcMillis(reader.GetInt64(18)),
        reader.IsDBNull(19) ? null : reader.GetString(19));

    private static (string Where, List<(string, object?)> Parameters) BuildFilter(
        string targetColumn, string? targetId, DateTimeOffset? start, DateTimeOffset? end)
    {
        var conditions = new List<string>();
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            conditions.Add($"{targetColumn} = $p0");
            parameters.Add(("$p0", targetId));
        }

        if (start is not null)
        {
            conditions.Add("timestamp_ms >= $p1");
            parameters.Add(("$p1", PersistenceClock.ToUtcMillis(start.Value)));
        }

        if (end is not null)
        {
            conditions.Add("timestamp_ms <= $p2");
            parameters.Add(("$p2", PersistenceClock.ToUtcMillis(end.Value)));
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        return (where, parameters);
    }
}
using Microsoft.Data.Sqlite;
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Persistence;

/// <summary>
///     Deterministic, bounded retention cleanup: deletes expired monitoring results, transitions, resolved alerts and
///     their occurrences. Never deletes active (Open/Acknowledged) alerts. Returns the total rows removed.
/// </summary>
public sealed class SqliteRetentionService : IDataRetentionService
{
    private readonly SqliteDatabase _db;

    public SqliteRetentionService(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<int> RunCleanupAsync(RetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var now = PersistenceClock.ToUtcMillis(DateTimeOffset.UtcNow);

        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var deleted = 0;

        deleted += ExecuteDelete(connection,
            "DELETE FROM monitoring_results WHERE timestamp_ms < $cutoff;",
            ("$cutoff", now - (long)policy.MonitoringResultRetention.TotalMilliseconds));

        deleted += ExecuteDelete(connection,
            "DELETE FROM state_transitions WHERE timestamp_ms < $cutoff;",
            ("$cutoff", now - (long)policy.StateTransitionRetention.TotalMilliseconds));

        // Delete occurrences of resolved alerts older than the retention window (their alert rows go next).
        deleted += ExecuteDelete(connection,
            """
            DELETE FROM alert_occurrences
            WHERE alert_id IN (
                SELECT alert_id FROM alerts
                WHERE status = 2 AND resolved_at_ms IS NOT NULL AND resolved_at_ms < $cutoff
            );
            """,
            ("$cutoff", now - (long)policy.ResolvedAlertRetention.TotalMilliseconds));

        // Delete old RESOLVED alerts only — active (Open/Acknowledged) alerts are always preserved.
        deleted += ExecuteDelete(connection,
            "DELETE FROM alerts WHERE status = 2 AND resolved_at_ms IS NOT NULL AND resolved_at_ms < $cutoff;",
            ("$cutoff", now - (long)policy.ResolvedAlertRetention.TotalMilliseconds));

        return deleted;
    }

    private static int ExecuteDelete(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);

        return command.ExecuteNonQuery();
    }
}
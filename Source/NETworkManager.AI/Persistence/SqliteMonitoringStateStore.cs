using System.Text.Json;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Persistence;

/// <summary>
///     Persistent monitoring state store: keeps an in-memory cache for the engine's hot path (latest result/health),
///     and appends every result + health transition to SQLite as historical evidence. Degrades to in-memory-only if
///     the database is unavailable (flagged via <see cref="PersistenceFaulted"/>).
/// </summary>
public sealed class SqliteMonitoringStateStore : IMonitoringStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SqliteDatabase _db;
    private readonly MonitoringStateStore _memory = new();

    private volatile bool _faulted;

    public SqliteMonitoringStateStore(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>True once a persistence write has failed (the store keeps working in-memory).</summary>
    public bool PersistenceFaulted => _faulted;

    public void Record(MonitoringResult result)
    {
        _memory.Record(result);

        try
        {
            PersistResult(result);
        }
        catch
        {
            _faulted = true;
        }
    }

    public void SetHealth(string targetId, NetworkHealthStatus health, DateTimeOffset timestamp)
    {
        var previous = _memory.GetHealth(targetId);
        _memory.SetHealth(targetId, health, timestamp);

        if (health == previous)
            return; // only persist actual transitions

        try
        {
            PersistTransition(targetId, timestamp, previous, health);
        }
        catch
        {
            _faulted = true;
        }
    }

    public NetworkHealthStatus GetHealth(string targetId) => _memory.GetHealth(targetId);

    public IReadOnlyList<MonitoringResult> GetLatestResults(string targetId) => _memory.GetLatestResults(targetId);

    public IReadOnlyList<MonitoringResult> GetRecentFailures(int maxCount) => _memory.GetRecentFailures(maxCount);

    public void RemoveTarget(string targetId) => _memory.RemoveTarget(targetId);

    public void Clear() => _memory.Clear();

    private void PersistResult(MonitoringResult result)
    {
        using var connection = _db.Open();
        connection.Open();

        SqliteDatabase.Execute(connection,
            """
            INSERT INTO monitoring_results
                (target_id, check_id, check_type, status, error_classification, timestamp_ms, duration_ms, safe_message, observed_json, correlation_id)
            VALUES
                ($targetId, $checkId, $checkType, $status, $errorClass, $ts, $duration, $message, $observed, $correlationId);
            """,
            ("$targetId", result.TargetId),
            ("$checkId", result.CheckId),
            ("$checkType", (int)result.CheckType),
            ("$status", (int)result.Status),
            ("$errorClass", (int)result.ErrorClassification),
            ("$ts", PersistenceClock.ToUtcMillis(result.Timestamp)),
            ("$duration", (long)result.Duration.TotalMilliseconds),
            ("$message", result.SafeMessage),
            ("$observed", SerializeObserved(result.Observed)),
            ("$correlationId", result.CorrelationId));
    }

    private void PersistTransition(string targetId, DateTimeOffset timestamp, NetworkHealthStatus previous, NetworkHealthStatus next)
    {
        using var connection = _db.Open();
        connection.Open();

        SqliteDatabase.Execute(connection,
            """
            INSERT INTO state_transitions (target_id, timestamp_ms, previous_state, new_state)
            VALUES ($targetId, $ts, $previous, $next);
            """,
            ("$targetId", targetId),
            ("$ts", PersistenceClock.ToUtcMillis(timestamp)),
            ("$previous", (int)previous),
            ("$next", (int)next));
    }

    private static string? SerializeObserved(object? observed)
    {
        if (observed is null)
            return null;

        try
        {
            return JsonSerializer.Serialize(observed, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
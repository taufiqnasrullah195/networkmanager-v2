using Microsoft.Data.Sqlite;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Persistence;

/// <summary>
///     Persistent alert store: in-memory cache for fast queries, with every alert lifecycle change (create/update/
///     acknowledge/resolve) persisted to SQLite and per-cycle occurrences written to a separate table. Restores active
///     alerts on startup via <see cref="LoadActiveAlertsAsync"/>. Degrades to in-memory if the database is unavailable.
/// </summary>
public sealed class SqliteAlertStore : IAlertStore
{
    private readonly SqliteDatabase _db;
    private readonly AlertStore _memory = new();

    private volatile bool _faulted;

    public SqliteAlertStore(SqliteDatabase db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public bool PersistenceFaulted => _faulted;

    public Alert CreateAlert(Alert alert)
    {
        var stored = _memory.CreateAlert(alert);

        try
        {
            PersistAlert(stored);
        }
        catch
        {
            _faulted = true;
        }

        return stored;
    }

    public bool UpdateAlert(Alert alert)
    {
        var previous = _memory.GetAlert(alert.AlertId);
        var updated = _memory.UpdateAlert(alert);

        if (!updated)
            return false;

        try
        {
            if (previous is not null && alert.OccurrenceCount > previous.OccurrenceCount)
                PersistOccurrence(alert.AlertId, alert.LastSeenAt, alert.Reason);

            PersistAlert(alert);
        }
        catch
        {
            _faulted = true;
        }

        return true;
    }

    public Alert? GetAlert(string alertId) => _memory.GetAlert(alertId);

    public IReadOnlyList<Alert> GetActiveAlerts() => _memory.GetActiveAlerts();

    public IReadOnlyList<Alert> GetRecentAlerts(int maxCount) => _memory.GetRecentAlerts(maxCount);

    /// <summary>Restores active (Open/Acknowledged) alerts from the database on startup.</summary>
    public async Task LoadActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _db.Open();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT alert_id, fingerprint, target_id, target_name, profile_id, severity, status, title, description,
                   reason, evidence, first_seen_ms, last_seen_ms, occurrence_count, previous_health, current_health,
                   failure_classification, acknowledged_at_ms, resolved_at_ms, resolution_evidence
            FROM alerts
            WHERE status IN (0, 1)
            ORDER BY last_seen_ms DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var alert = ReadAlert(reader);
            _memory.CreateAlert(alert);
        }
    }

    private void PersistAlert(Alert alert)
    {
        using var connection = _db.Open();
        connection.Open();

        SqliteDatabase.Execute(connection,
            """
            INSERT INTO alerts
                (alert_id, fingerprint, target_id, target_name, profile_id, severity, status, title, description,
                 reason, evidence, first_seen_ms, last_seen_ms, occurrence_count, previous_health, current_health,
                 failure_classification, acknowledged_at_ms, resolved_at_ms, resolution_evidence)
            VALUES
                ($alertId, $fingerprint, $targetId, $targetName, $profileId, $severity, $status, $title, $description,
                 $reason, $evidence, $firstSeen, $lastSeen, $occurrenceCount, $prevHealth, $currHealth,
                 $failureClass, $ackAt, $resolvedAt, $resolutionEvidence)
            ON CONFLICT(alert_id) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                target_id = excluded.target_id,
                target_name = excluded.target_name,
                profile_id = excluded.profile_id,
                severity = excluded.severity,
                status = excluded.status,
                title = excluded.title,
                description = excluded.description,
                reason = excluded.reason,
                evidence = excluded.evidence,
                first_seen_ms = excluded.first_seen_ms,
                last_seen_ms = excluded.last_seen_ms,
                occurrence_count = excluded.occurrence_count,
                previous_health = excluded.previous_health,
                current_health = excluded.current_health,
                failure_classification = excluded.failure_classification,
                acknowledged_at_ms = excluded.acknowledged_at_ms,
                resolved_at_ms = excluded.resolved_at_ms,
                resolution_evidence = excluded.resolution_evidence;
            """,
            ("$alertId", alert.AlertId),
            ("$fingerprint", alert.Fingerprint),
            ("$targetId", alert.TargetId),
            ("$targetName", alert.TargetName),
            ("$profileId", alert.ProfileId),
            ("$severity", (int)alert.Severity),
            ("$status", (int)alert.Status),
            ("$title", alert.Title),
            ("$description", alert.Description),
            ("$reason", alert.Reason),
            ("$evidence", alert.Evidence),
            ("$firstSeen", PersistenceClock.ToUtcMillis(alert.FirstSeenAt)),
            ("$lastSeen", PersistenceClock.ToUtcMillis(alert.LastSeenAt)),
            ("$occurrenceCount", alert.OccurrenceCount),
            ("$prevHealth", (int)alert.PreviousHealthState),
            ("$currHealth", (int)alert.CurrentHealthState),
            ("$failureClass", alert.FailureClassification),
            ("$ackAt", alert.AcknowledgedAt is null ? null : PersistenceClock.ToUtcMillis(alert.AcknowledgedAt.Value)),
            ("$resolvedAt", alert.ResolvedAt is null ? null : PersistenceClock.ToUtcMillis(alert.ResolvedAt.Value)),
            ("$resolutionEvidence", alert.ResolutionEvidence));
    }

    private void PersistOccurrence(string alertId, DateTimeOffset timestamp, string? reason)
    {
        using var connection = _db.Open();
        connection.Open();

        SqliteDatabase.Execute(connection,
            """
            INSERT INTO alert_occurrences (alert_id, timestamp_ms, reason)
            VALUES ($alertId, $ts, $reason);
            """,
            ("$alertId", alertId),
            ("$ts", PersistenceClock.ToUtcMillis(timestamp)),
            ("$reason", reason));
    }

    private static Alert ReadAlert(SqliteDataReader reader) => new()
    {
        AlertId = reader.GetString(0),
        Fingerprint = reader.GetString(1),
        TargetId = reader.GetString(2),
        TargetName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        ProfileId = reader.IsDBNull(4) ? null : reader.GetString(4),
        Severity = (AlertSeverity)reader.GetInt32(5),
        Status = (AlertStatus)reader.GetInt32(6),
        Title = reader.GetString(7),
        Description = reader.IsDBNull(8) ? null : reader.GetString(8),
        Reason = reader.IsDBNull(9) ? null : reader.GetString(9),
        Evidence = reader.IsDBNull(10) ? null : reader.GetString(10),
        FirstSeenAt = PersistenceClock.FromUtcMillis(reader.GetInt64(11)),
        LastSeenAt = PersistenceClock.FromUtcMillis(reader.GetInt64(12)),
        OccurrenceCount = reader.GetInt32(13),
        PreviousHealthState = (NetworkHealthStatus)reader.GetInt32(14),
        CurrentHealthState = (NetworkHealthStatus)reader.GetInt32(15),
        FailureClassification = reader.IsDBNull(16) ? null : reader.GetString(16),
        AcknowledgedAt = reader.IsDBNull(17) ? null : PersistenceClock.FromUtcMillis(reader.GetInt64(17)),
        ResolvedAt = reader.IsDBNull(18) ? null : PersistenceClock.FromUtcMillis(reader.GetInt64(18)),
        ResolutionEvidence = reader.IsDBNull(19) ? null : reader.GetString(19),
    };
}
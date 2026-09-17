# TheWiseNetwork — Persistent State Store & History (Step 12)

STEP 12 replaces the temporary in-memory monitoring/alert state with a **persistent** layer, so monitoring and
alert history survive application restarts. It is the persistence foundation for future trends, dashboards, SLA
calculations, and AI historical reasoning — **not** analytics.

## Golden rule

**Configuration tells the system what to monitor. Monitoring produces observations. Health evaluation produces
state. Alerts represent meaningful state changes. Persistent storage preserves evidence. AI interprets evidence.**
Historical data is never presented as current state without making the time distinction explicit.

## Storage technology & location

- **SQLite** (`Microsoft.Data.Sqlite` 10.0.x, bundled native `e_sqlite3`) — an embedded, zero-configuration local
  database appropriate for a Windows desktop app. The repository had no prior database/ORM, so this is the single
  storage technology, used behind provider-neutral interfaces.
- **Location**: `%LocalAppData%\NETworkManager\AI\thewisenetwork.db` (per-user, outside the install dir and the
  repo). The path is passed into `SqliteDatabase` by the composition root — never hardcoded in the library.

## What is persisted (and what is not)

| Layer | Persistence |
| --- | --- |
| **Configuration** (profiles/targets/checks) | existing JSON repository (`MonitoringProfileService`), unchanged |
| **Current state** (latest result/health) | in-memory cache (fast hot path) + results written to SQLite |
| **Historical evidence** (results, transitions) | SQLite `monitoring_results` + `state_transitions` |
| **Alerts** (lifecycle + occurrences) | SQLite `alerts` + `alert_occurrences` |

Configuration stays in its JSON store deliberately — the spec's own design distinction keeps CONFIGURATION separate
from CURRENT STATE / HISTORICAL EVIDENCE, and duplicating it into SQLite would create two sources of truth.

## Schema (version 1, `PRAGMA user_version`)

- `monitoring_results(id, target_id, check_id, check_type, status, error_classification, timestamp_ms,
  duration_ms, safe_message, observed_json, correlation_id)` — indexes on `(target_id, timestamp_ms)` and
  `(check_id, timestamp_ms)`.
- `state_transitions(id, target_id, timestamp_ms, previous_state, new_state, reason)` — index on
  `(target_id, timestamp_ms)`.
- `alerts(alert_id PK, fingerprint, target_id, target_name, profile_id, severity, status, title, description,
  reason, evidence, first_seen_ms, last_seen_ms, occurrence_count, previous_health, current_health,
  failure_classification, acknowledged_at_ms, resolved_at_ms, resolution_evidence)` — indexes on
  `(status, last_seen_ms)` and `(target_id, last_seen_ms)`.
- `alert_occurrences(id, alert_id, timestamp_ms, reason)` — index on `(alert_id, timestamp_ms)`.

Timestamps are **UTC Unix-epoch milliseconds (INTEGER)** for indexable range queries; conversion to local time
happens only at the UI boundary. Enums are stored as integers.

## Repositories & stores

- `SqliteDatabase` — connection management, schema creation, versioned migrations, parameterized execution.
- `SqliteMonitoringStateStore : IMonitoringStateStore` — in-memory cache (reuses `MonitoringStateStore`) + appends
  every result and (on actual change) every transition to SQLite. Degrades to in-memory if the DB is unavailable.
- `SqliteAlertStore : IAlertStore` — in-memory cache (reuses `AlertStore`) + upserts alert lifecycle + writes per-cycle
  occurrences; `LoadActiveAlertsAsync()` restores Open/Acknowledged alerts on startup. Degrades to in-memory.
- `SqliteHistoryRepository : IMonitoringHistoryRepository, IAlertHistoryRepository` — bounded, indexed, paginated,
  parameterized read queries (results, transitions, alert history, occurrences).
- `SqliteRetentionService : IDataRetentionService` — deletes expired results (30d), transitions (90d), and old
  **resolved** alerts + their occurrences (180d); never touches active (Open/Acknowledged) alerts. Cancellable.

## Query API & pagination

`GetResultsAsync` / `GetTransitionsAsync` / `GetAlertHistoryAsync` take optional `targetId`, `start`, `end`,
`limit`, `offset` — results are bounded (`LIMIT/OFFSET`), never a full-table load. `GetOccurrencesAsync(alertId,
limit, offset)` pages occurrences.

## Startup recovery & restart

On startup, `PersistenceComposition.InitializeAsync()` opens/migrates the DB and restores **active alerts**.
Persisted health/results are **not** replayed as current state — fresh monitoring checks establish current health
after monitoring starts. A persisted `UNHEALTHY` + active alert before shutdown is restored as an active alert
after restart; a subsequent recovery transition resolves it with fresh evidence (asserted by tests).

## Migration & versioning

`PRAGMA user_version` (currently 1). `InitializeAsync` creates the schema on first run, applies future additive
migrations, and **rejects** a database written by a newer schema version (instead of corrupting it). Initialization
failure is reported (`"Monitoring history storage is unavailable."`) and the app continues in in-memory mode —
never silently losing data without telling the user.

## Security

No credentials, keys, tokens, or secrets anywhere in the schema. All SQL is **parameterized** (no string
concatenation with user-controlled values). Observed tool data is stored as JSON but contains only secret-free
monitoring fields. Timestamps are UTC.

## WPF integration

`NetworkMonitoringView` gains a minimal **History** panel (recent monitoring results + recent alert history with a
Refresh) driven by `PersistenceComposition.History`. `PersistenceComposition` initializes the DB and exposes the
persistent stores to the monitoring/alert engines (which fall back to in-memory if it is unavailable).

## AI read-only integration

Two new tools, both `IAlertHistoryRepository`/`IMonitoringHistoryRepository`-only (no mutation):
- `network_monitoring_history` — bounded historical results + transitions (facts only).
- `network_alert_history` — bounded alert lifecycle history (facts only).

The AI can ask "what happened to the gateway in the last hour" / "was this already resolved before" and receives
structured historical evidence, clearly distinct from current state. The persistence layer generates no conclusions.

## Performance

WAL journal mode, indexes on the hot query columns, `LIMIT/OFFSET` bounded reads, async read operations, retention
cleanup off the UI thread. Per-result writes are small single INSERTs on the background scheduler thread.

## Limitations

- No advanced analytics, SLA, or charts (out of scope).
- No notification providers, SNMP, or incident management (out of scope).
- SQLite is a single-file embedded DB; multi-process concurrent access is not a target.
- `ProfileId`/`ProfileName` on alerts remain unpopulated (monitoring is profile-agnostic per target).
- Configuration remains JSON (not SQLite) by design.

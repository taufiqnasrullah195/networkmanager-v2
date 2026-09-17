# TheWiseNetwork — Network Health Dashboard & Visualization (Step 14)

STEP 14 adds a presentation-only Network Health Dashboard. It aggregates existing monitoring, alert, history, and
SNMP services into a single operational view. It owns **no** domain logic: it does not monitor, diagnose, configure,
or remediate — it presents evidence.

## Golden rule

The dashboard presents evidence produced by Monitoring, SNMP, Alerts, History (and later Discovery/Topology). It
distinguishes CURRENT STATE (device = HEALTHY), HISTORICAL STATE (device was UNHEALTHY 10:32–10:47), RAW TELEMETRY
(ICMP latency 4 ms), ALERT (connectivity failure — RESOLVED), and AI INFERENCE (interpretation). The dashboard never
turns telemetry into an unsupported conclusion.

## Architecture

```
NetworkHealthDashboardView ── NetworkHealthDashboardViewModel ── DashboardAggregator
                                                                    │
                          ┌──────────────────┬──────────────┬───────┴──────┐
                          ▼                  ▼              ▼              ▼
                    IMonitoringQuery    IAlertQuery    history repos   ISnmpTelemetryRepository
```

- `DashboardAggregator` (cross-platform, in `NETworkManager.AI/Dashboard`) consumes the read surfaces and computes a
  `DashboardSnapshot`. It performs no network calls, no writes, no AI.
- `NetworkHealthDashboardViewModel` (WPF) is a thin projection of the snapshot into display rows + filters + search.
- `NetworkHealthDashboardView` (WPF) binds the VM; `DashboardComposition` wires the aggregator to the live services.

## Data sources

- **Health summary / device table** — `IMonitoringQuery.GetCurrentStatus()` (current state).
- **Device type/address** — profile targets (`IMonitoringProfileService`), flattened by `DashboardComposition`.
- **Active alerts** — `IAlertQuery.GetActiveAlerts()`.
- **Recent events** — state transitions (`IMonitoringHistoryRepository`) + recent failures (`GetRecentFailures`) +
  active alerts.
- **Interface health / issues** — `ISnmpTelemetryRepository.GetAllLatestInterfaceTelemetryAsync`.
- **Latency** — current ping telemetry (`MonitoringResult.Observed as PingResult`).
- **Availability / trends** — bounded history queries (`IMonitoringHistoryRepository` / `IAlertHistoryRepository`).

## Health summary

Counts of HEALTHY / DEGRADED / UNHEALTHY / UNKNOWN / STALE devices, active-alert count, and monitoring RUNNING/
STOPPED — all from current state. Health is never derived from alert count alone.

## Device health table

Device, type, address, health, last check, SNMP availability, active-alert count. Filterable by status and searchable
by name/id/address (in-memory, bounded — no per-keystroke database query).

## Interface health

UP / DOWN / ADMIN-DOWN / UNKNOWN counts from latest SNMP interface telemetry. Admin-Down is counted separately and is
**not** classified as a failure. Top issues (operational DOWN, errors, discards) are listed only when actual telemetry
supports them.

## Alerts

Active alerts are shown prominently (severity, target, occurrence count, last-seen). Clicking navigates to the
existing STEP 11 alert UI — no second alert-detail implementation.

## Historical metrics

- **Availability** = healthy observations / total valid (non-UNKNOWN) observations over a window. It is presented as
  an availability of observations, **not** an SLA. "Insufficient data" is shown when there are no valid observations.
- **Latency** = current average latency + packet loss from ping telemetry (min/max not in the current ping model).
- **Health trend** = a state timeline (step function) from persisted transitions — never a fake continuous line.
- **Alert trend** = created/resolved/active counts from persisted alert history.

All historical queries are bounded (`HistoryQueryLimit`, no unlimited loads).

## Stale data & freshness

A target is shown STALE when its last check is older than `DashboardOptions.StalenessThreshold` (default 2 minutes =
multiple missed 30s intervals). The dashboard shows STALE (not a stale HEALTHY), plus a global "Last updated" time and
per-device "last check" time. Monitoring STOPPED is shown explicitly — the dashboard never implies active monitoring.

## Event-driven updates & refresh

The dashboard refreshes on demand (a Refresh button) and on filter/search changes. It does **not** run an aggressive
polling loop; the monitoring engine's own scheduler remains the only poller. (Subscribing the VM to the existing
`IMonitoringObserver`/`IAlertObserver` events for incremental updates is a follow-up; the current implementation
re-aggregates bounded in-memory data on refresh.)

## AI integration

"Analyze with AI" navigates to the existing AI Copilot view with a contextual prompt (e.g. "Analyze the current health
of Gateway using available monitoring evidence."). The dashboard has **no** direct AI, database, or UI-control access;
AI remains behind `IAIConversationService` → Tool Orchestrator → Tool Policy. The dashboard cannot modify monitoring
configuration, alerts, or SNMP configuration.

## Performance

- Async loading; bounded queries (`MaxDevices`, `MaxInterfaces`, `HistoryQueryLimit`).
- In-memory filter/search (no database round-trip per click/keystroke).
- Snapshot aggregation is cheap (current state is already in memory).

## Security

The dashboard displays no credentials, community strings, SNMPv3 passwords, API keys, or authorization headers. The
snapshot records are secret-free (asserted by tests). Historical data contains no secrets (asserted in Step 12/13).

## Limitations

- No charts yet (state timeline / counters are text-based; a charting library is a follow-up).
- Availability/latency are per-target summaries, not charts; no continuous numeric series.
- "Analyze with AI" pre-fills on first navigation (subsequent navigations reuse the existing copilot view).
- Device detail / interface detail navigation is not implemented (the aggregator exposes the data; a detail view is a
  follow-up).
- Discovery/topology are not yet integrated (those modules are upstream, not yet in this fork).

# TheWiseNetwork — Alert Engine & Health State Notifications (Step 11)

STEP 11 turns meaningful monitoring health-state changes into **structured alerts**. The alert engine consumes
the events produced by the monitoring engine (Step 9/10); it detects, evaluates, creates, updates, and resolves
alerts — it never remediates, never calls AI, and never notifies external channels (those are future consumers).

## Golden rule

**Monitoring detects. HealthEvaluator evaluates. AlertEngine creates structured alerts. AlertStore maintains alert
state. Future notification systems deliver. AI analyzes evidence. No component remediates. The user stays in
control.**

## Architecture

```
MonitoringEngine ──► HealthEvaluator ──► HealthStateChanged ──► AlertEngine
                                                                  │
                                                    ┌─────────────┴─────────────┐
                                                    ▼                           ▼
                                               AlertEvaluator               AlertStore
                                               (deterministic rules)        (in-memory)
                                                                                 │
                                                                                 ▼
                                                                        Alert / AlertEvent
                                                                                 │
                                                          ┌──────────────────────┼──────────────────┐
                                                          ▼                      ▼                  ▼
                                                         WPF             Future notification    AI
                                                       (panel)               engine          (network_alerts)
```

## Domain model

- **`Alert`** — `AlertId`, `Fingerprint` (dedup key), `TargetId`/`TargetName`, optional `ProfileId`/`ProfileName`
  (unpopulated this step — the monitoring engine is profile-agnostic per target), `Severity`, `Status`, `Title`,
  `Description`, `Reason`, `Evidence`, `FirstSeenAt`, `LastSeenAt`, `OccurrenceCount`, `PreviousHealthState`,
  `CurrentHealthState`, `FailureClassification`, `ResolvedAt`, `ResolutionEvidence`.
- **`AlertSeverity`** — `Info`/`Warning`/`Error`/`Critical` (CRITICAL reserved for a future explicit rule).
- **`AlertStatus`** — `Open` → `Acknowledged` → `Resolved` (plus reserved `Suppressed`).

## Rules (deterministic, `AlertEvaluator`)

| Transition | Decision |
| --- | --- |
| HEALTHY → UNHEALTHY | create `Error` alert (dedup → update if active) |
| HEALTHY → DEGRADED | create `Warning` alert |
| DEGRADED → UNHEALTHY | escalate to `Error` |
| UNHEALTHY → DEGRADED | downgrade to `Warning` |
| UNHEALTHY → HEALTHY | resolve target's active alerts |
| DEGRADED → HEALTHY | resolve target's active alerts |
| UNKNOWN → * | ignore (no healthy baseline) |

Severity is derived from the transition, never from an AI. `AlertOptions` gates each rule
(`AlertingEnabled`, `CreateAlertOnDegraded`, `CreateAlertOnUnhealthy`, `AutoResolveOnRecovery`,
`DeduplicationEnabled`).

## Deduplication & occurrence tracking

The fingerprint is deterministic: `TargetId + CheckType` (never display text). Repeated failures of the same
target/check update the one active alert — `OccurrenceCount` increments and `LastSeenAt` advances — instead of
spawning a new alert every polling cycle. Recurring failures arrive as `MonitoringCompleted` results (the
`MonitoringEvent.Result` payload added in this step); state *transitions* arrive as `HealthStateChanged`. The
engine therefore keys creation off transitions and occurrence off recurring results.

## Lifecycle, acknowledgement, resolution

- **Acknowledgement** (`Open → Acknowledged`) is a user action. It does not resolve, stop, or hide anything; a
  recovered target still resolves an acknowledged alert (`Acknowledged → Resolved`).
- **Resolution** is evidence-driven: the recovery transition carries the result whose `SafeMessage` becomes
  `ResolutionEvidence` (e.g. "ICMP echo reply received."). `ResolvedAt` is recorded.
- **Flapping**: every meaningful transition is recorded (create/resolve/create); `FirstSeenAt`/`LastSeenAt`/
  `OccurrenceCount` give a future notification layer what it needs to suppress noise. A `IAlertSuppressionPolicy`
  boundary is present (no-op this step) for maintenance windows/muted targets later.

## Events & observers

`IAlertObserver` + `AlertEvent` (`AlertCreated`/`AlertUpdated`/`AlertAcknowledged`/`AlertResolved`) mirror the
monitoring observer pattern. The engine is not coupled to WPF — the WPF panel, a future notification engine, and
logging all subscribe through the same abstraction.

## Store, logging, concurrency, shutdown

- `IAlertStore` (in-memory `AlertStore`, bounded) — `Create/Update/Get/GetActive/GetRecent`; a durable store can
  replace it later.
- `IAlertLogger` — logs create/update/acknowledge/resolve + rule errors; never credentials or polling noise.
- Concurrency: a mutation lock makes dedup atomic, so simultaneous state changes cannot create duplicate alerts.
- Lifecycle: `Start`/`Stop` — when stopped, the engine ignores all input (graceful shutdown).

## WPF integration

A minimal alerts panel in `NetworkMonitoringView`: active-alert list (severity/target/status/title) + detail
(transition, occurrences, first/last seen, reason, evidence, resolution) + an **Acknowledge** button. It consumes
`AlertComposition.Alerts` (singleton `AlertEngine`), which the monitoring view attaches to the monitoring engine
when monitoring starts.

## AI read-only integration

`network_alerts` (`NetworkAlertsTool`) depends solely on `IAlertQuery`. It returns structured
`NetworkAlertsResult` (facts + evidence only — the engine never fabricates inference/recommendation). The AI can
ask "what systems are unhealthy", "any active alerts", "what changed" — but cannot acknowledge, resolve, delete,
suppress, or modify rules. Configuration remains UI-only.

## Security

Alerts contain no credentials; `Title`/`Description`/`Reason`/`Evidence` are built from secret-free monitoring
fields (asserted by tests). Logging takes ids/severity only. No remediation, no shell, no network writes, no AI
calls from the engine.

SNMP (Step 13) integrates through the same path: an `SnmpTelemetry` check that fails (timeout) or degrades produces
a monitoring result whose `CheckType` feeds the alert fingerprint — but SNMP interface status/counters are evidence,
never an automatic alert. See `docs/SNMP_TELEMETRY.md`.

The Network Health Dashboard (Step 14, `docs/NETWORK_HEALTH_DASHBOARD.md`) surfaces active alerts read-only; clicking
an alert reuses this step's alert detail UI.

## Limitations

- `ProfileId`/`ProfileName` are unpopulated (the monitoring engine doesn't propagate per-target profile identity).
- Flapping *suppression* is not implemented (the metadata + `IAlertSuppressionPolicy` boundary are ready).
- `Critical` severity is unused until an explicit rule is added.
- In-memory store only; no persistence. The WPF panel is CI-build-verified (manual visual check needs Windows).

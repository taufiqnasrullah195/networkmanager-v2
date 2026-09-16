# TheWiseNetwork — Network Monitoring & Health Engine (Step 9)

STEP 9 evolves the platform from *"run a diagnostic when the user asks"* to *"continuously observe authorized
targets and compute structured network health."* This is the foundation for future SNMP telemetry, a health
dashboard, alerting, historical evidence, and AI incident analysis — without rewriting the core engine.

## Golden rule

**The monitoring engine observes. The health evaluator interprets structured measurements. The AI explains
evidence. The user remains in control. Monitoring never silently becomes remediation.**

All checks are read-only. There is no remediation, no configuration change, no shell, and no AI call from the
monitoring engine.

## Architecture

```
WPF (NetworkMonitoringView)                      AI Copilot (network_monitoring_status tool)
        │                                                    │
        ▼                                                    ▼
 NetworkMonitoringViewModel ──► IMonitoringEngine ◄── IMonitoringQuery (read-only surface)
                                    │
                     ┌──────────────┴───────────────┐
                     ▼                              ▼
           MonitoringScheduler               MonitoringStateStore
           (one loop + SemaphoreSlim)        (in-memory, latest + health + failures)
                     │
                     ▼
            MonitoringCheckExecutor
                     │
        ┌────────────┼───────────────┐
        ▼            ▼               ▼
   ping (PingTool)  tcp_test       dns_lookup      ← reuses Step 3 tool wrappers (no duplicate networking)
        │            (TcpTestTool)   (DnsLookupTool)
        └────────────┼───────────────┘
                     ▼
              MonitoringResult  ──►  HealthEvaluator  ──►  NetworkHealthStatus  ──►  MonitoringEvent (observer)
```

## Domain models (`NETworkManager.AI/Monitoring/`)

- **`MonitoringTarget`** — stable `Id`, optional `Name`, `Hostname`/`IPAddress` (never assumed to be an IP),
  `Type` (`Host`/`Server`/`Router`/`Switch`/`NetworkDevice`/`Service`/`Other`), `Enabled`, `Description`.
- **`MonitoringCheck`** — `CheckId`, `Type` (`Ping`/`TcpConnectivity`/`DnsResolution`), `TargetId`, `Port`,
  nullable `Timeout`/`Interval` (inherit engine defaults), `Enabled`, `Expected`, secret-free `Metadata`.
- **`MonitoringResult`** — `Status`, `ErrorClassification`, `Timestamp`, `Duration`, `SafeMessage`,
  `Observed` (typed tool output), `Evidence` (source-identified), `CorrelationId`.
- **`MonitoringEvidence`** — identifies its **source** (`NetworkTool.ping`, `NetworkTool.tcp_test`,
  `NetworkTool.dns_lookup`), so no layer downstream can invent evidence.
- **`MonitoringSnapshot`** — one target's current health + latest results; `HealthStateChange` captures a
  transition with previous/new state + reason/evidence; `MonitoringEvent` is the observer payload.

### Statuses are not collapsed

| `MonitoringResultStatus` | Meaning |
| --- | --- |
| `Healthy` / `Warning` / `Unhealthy` | measurement succeeded / degraded evidence / confirmed failure |
| `Timeout` | no answer within the bound — **must not** be read as "down" |
| `Error` / `Cancelled` / `Unknown` / `Running` | operational error / caller cancelled / no evidence yet |

`MonitorErrorClass` keeps the finer branch: `NameResolution`, `Unreachable`, `IcmpBlocked` (reserved),
`TcpConnectivity`, `DnsResolution`, `Timeout`, `Cancelled`, `ExecutionError`. DNS timeout vs fault vs
unreachable are kept distinct; **"ICMP blocked" is not inferred as "host down"** — the message says
*"ICMP echo request got no reply; host availability could not be confirmed via ICMP."*

## Health evaluation (`HealthEvaluator`)

Deterministic, measurement-only:

- Single result → `Healthy`, `Warning`/`Timeout` → `Degraded`, `Unhealthy`/`Error` → `Unhealthy`, else `Unknown`.
- Aggregate = worst non-`Unknown` contribution: any `Unhealthy` → `Unhealthy`; else any `Degraded` → `Degraded`;
  all `Healthy` → `Healthy`; no evidence → `Unknown`. A cancelled/timeout run contributes **no firm claim**;
  it never fabricates a "down".

## Engine + scheduling

- `IMonitoringEngine` (`MonitoringEngine`): `StartAsync`/`StopAsync`, `AddTarget`/`RemoveTarget`,
  `SetTargetEnabled`, `AddCheck`/`RemoveCheck`, `RunCheckAsync` (single-shot), `Subscribe`/`Unsubscribe`,
  and the read-only `IMonitoringQuery` surface (`GetCurrentStatus`, `GetHealth`, `GetLatest`,
  `GetRecentFailures`).
- `MonitoringScheduler`: **one consolidated loop** (no task-per-target), a `SemaphoreSlim(maxConcurrency)`
  cap, `Interval`-driven `Task.Delay` idle, and graceful shutdown (cancel → in-flight checks finish as
  `Cancelled`, no stuck tasks).
- Cancellation is **gated before dispatch** (a pre-cancelled token yields `Cancelled` even for an instant fake
  tool). Every check has a timeout: the check `Timeout` is passed into the tool input, the execution service
  enforces the tool `Timeout`, and the executor adds a defensive `WaitAsync` outer bound.
- Options (`MonitoringOptions`): `DefaultInterval` 30s, `DefaultTimeout` 5s, `MaxConcurrency` 5,
  `MaxRecentFailures` 100. All configurable, sensible defaults, no hard-coded target addresses.

## Check execution (reuses Step 3 tools)

`MonitoringCheckExecutor` maps each check to the **existing** `ping`/`tcp_test`/`dns_lookup` tools via
`IToolExecutionService` — no duplicate networking, no AI, no shell:

- **PING** → `PingResult` (now normalized with `TimedOutCount`): full replies → `Healthy`; partial loss →
  `Warning`; all-timeout → `Timeout`; zero-reply → `Unhealthy(Unreachable)`.
- **TCP** → `TcpTestResult.State`: `Open` → `Healthy`; `TimedOut` → `Timeout`; `Closed` →
  `Unhealthy(TcpConnectivity)` ("does not imply the application is down").
- **DNS** → `DnsLookupResult`: resolved → `Healthy`; failure → `Unhealthy(DnsResolution)` ("does not prove the
  server is down").

The upstream NETworkManager has a WPF **Ping Monitor** feature; this engine reuses the same underlying `Ping` /
`DNSLookup` engines through the Step 3 wrappers rather than duplicating them (the Ping Monitor is a
single-tool UI; this engine is a tool-agnostic orchestration adding TCP/DNS/health/scheduling).

## State, events, and persistence readiness

- `IMonitoringStateStore` (in-memory `MonitoringStateStore`): latest result per target/check-type, per-target
  health, bounded recent-failure buffer. A future durable store replaces it behind the same interface.
- `IMonitoringObserver` + `MonitoringEvent`: `MonitoringStarted`, `MonitoringStopped`,
  `MonitoringCompleted`, `MonitoringFailed`, `HealthStateChanged` (only on actual transitions — never a
  duplicate per poll). WPF, a future alert engine, and future AI subscribe through this abstraction.
- `MonitoringProfile` + `MonitoringProfileStore` (JSON, SHA-256 checksummed, no secrets) externalize targets,
  checks, and defaults. The engine itself keeps only in-memory state.

## AI integration (read-only)

`MonitoringStatusTool` (`network_monitoring_status`) is a **LOW-risk, no-approval** `INetworkTool` that depends
solely on `IMonitoringQuery`. It answers "which targets are unhealthy", "what is the state of X", and "show
recent failures" with structured `MonitoringStatusResult` — and cannot add/remove targets or change
configuration. The copilot's registry gains it in `AICopilotFactory`.

## WPF integration (minimal)

`NetworkMonitoringView` shows target / status / last-check / observed / last-error (a flat, header-aligning
table, matching the codebase's simple-view pattern). `NetworkMonitoringViewModel` subscribes as an observer
(marshalling events to the dispatcher) and projects `GetCurrentStatus` into display rows. The engine lifecycle
is view-scoped in this step (start on load, graceful stop on unload); a persistent background service + app-exit
hook is the follow-up.

## Security

- Read-only checks only; no remediation, no configuration change, no shell/PowerShell, no AI from the engine.
- No credentials anywhere (no SNMP yet); the profile JSON carries only targets/checks; `SafeMessage`/evidence
  are secret-free; lifecycle logging takes ids/status only (asserted by tests).
- Bounded concurrency, per-check timeouts, cancellation, idempotent start/stop, and no background task outliving
  `StopAsync`.

## Limitations

- SNMP/HTTP monitoring, persistent history, analytics, alerting, and dashboards are **out of scope** (next
  steps). Interactive profile editing and app-wide shutdown hooking are follow-ups; the profile is edited as JSON
  today.
- ICMP "blocked" vs "host down" cannot be machine-distinguished yet (`Ping` aggregates replies); the engine
  therefore reports `Unreachable` with cautious wording. DNS timeout vs non-existent name are folded into
  `DnsResolution` because the upstream DNS lookup engine exposes no machine-readable distinction.
# TheWiseNetwork — SNMP Device Telemetry & Network Metrics (Step 13)

STEP 13 adds a **strictly read-only** SNMP telemetry foundation. It collects structured, secret-free device
and interface telemetry from authorized SNMP-enabled devices, normalizes it, persists history, exposes read-only
AI tools, and feeds the existing monitoring/alert/persistence pipeline. It never performs a SET, never changes
device configuration, and never remediates.

## Golden rule

SNMP provides observations. The monitoring engine schedules collection. The normalizer produces structured
telemetry. The health evaluator interprets deterministic conditions. The alert engine reacts to meaningful state
changes. Persistent storage preserves evidence. AI analyzes evidence. **No telemetry value alone is turned into an
unsupported conclusion** — e.g. high traffic is an observation, not a failure; an interface `AdminStatus DOWN` is a
configuration observation, not a device failure.

## Existing SNMP support (upstream) — inspected first

NETworkManager already ships a full SNMP feature in `NETworkManager.Models` (`SNMPClient`, `SNMPOptions`,
`SNMPOptionsV3`, `SNMPViewModel`, profiles, an OID validator) using **Lextm.SharpSnmpLib 12.5.7**, covering v1/v2c/v3
**including SNMP SET** for the interactive SNMP tool. That code is:

- Windows-only (`net10.0-windows` project), so the cross-platform `NETworkManager.AI` library cannot reference it.
- Includes `SetAsync`/`SetAsyncV3`, which this step must **not** expose.

Therefore the telemetry subsystem reuses the **same library** (Lextm.SharpSnmpLib) behind a new read-only
provider boundary in `NETworkManager.AI` (`SharpSnmpProvider`), rather than wrapping `SNMPClient`. The upstream
SNMP tool is left untouched; its SET capability remains confined to the interactive SNMP application, never wired
into monitoring or AI.

## Supported SNMP versions

- **SNMPv1** — GET / GETNEXT walk (community).
- **SNMPv2c** — GET / GETNEXT walk (community). This is the default for initial read-only telemetry.
- **SNMPv3** — GET / GETBULK walk, with NoAuthNoPriv / AuthNoPriv / AuthPriv, MD5/SHA-1/SHA-256/SHA-384/SHA-512
  auth and DES/AES/AES-192/AES-256 privacy. v3 is genuinely implemented in `SharpSnmpProvider` (engine/time-window
  discovery is handled), not merely an abstraction.

There is deliberately **no SET** anywhere in this subsystem.

## Architecture

```
MonitoringEngine ── SNMP_TELEMETRY check ── ISnmpTelemetryCollector ── ISnmpProvider ── device (GET/WALK only)
                                                      │                      (Lextm.SharpSnmpLib)
                                                      ▼
                                              ISnmpNormalizer ── DeviceTelemetry / InterfaceTelemetry
                                                      │
                                                      ▼
                                              MonitoringResult ── HealthEvaluator ── AlertEngine ── PersistentStore
AI:  DeviceTelemetry / InterfaceTelemetry / history ── read-only tools ── ToolOrchestrator ── IAIConversationService
```

The rest of TheWiseNetwork depends only on `ISnmpProvider` (and the normalized models) — never on a third-party
SNMP library type. `Lextm.SharpSnmpLib` types never escape the provider into WPF, AI, the monitoring engine, or the
alert engine.

## Credentials & secure storage

- `SnmpConnectionConfig`/`SnmpCheckConfig` carry **no secrets** — only `Host`, `Port`, `Version`,
  `CredentialReference`, `CollectionMode`, `Timeout`, `Retries`.
- The credential material (`SnmpCredential`: community string, or SNMPv3 username/auth/privacy) is serialized to a
  single string and stored **encrypted** via the existing `ISecureCredentialStore` (DPAPI, `DpapiSecureCredentialStore`)
  under the `CredentialReference` key. `SnmpCredentialCodec` is the only serializer.
- Secrets never appear in source, Git, JSON configuration, monitoring profiles, the database, logs, AI context, or
  tool results. The UI shows only "✓ Configured", never the secret (asserted by tests).

## System telemetry (standard MIB-II)

`sysName` (1.3.6.1.2.1.1.5.0), `sysDescr` (1.3.6.1.2.1.1.1.0), `sysObjectID` (1.3.6.1.2.1.1.2.0),
`sysUpTime` (1.3.6.1.2.1.1.3.0, TimeTicks → duration). Missing OIDs are `null` ("unavailable"), never fabricated.
Uptime is an observation only — it is not treated as proof of health.

## Interface telemetry (standard IF-MIB)

`ifIndex`, `ifDescr`, `ifAdminStatus`, `ifOperStatus`, `ifSpeed`, `ifInOctets`, `ifOutOctets`, `ifInErrors`,
`ifOutErrors`, `ifInDiscards`, `ifOutDiscards`, plus the high-capacity `ifName` / `ifHCInOctets` / `ifHCOutOctets`
from ifXTable. **64-bit counters are preferred** when the device provides them; 32-bit counters are the fallback.
Admin and operational status are mapped to the RFC 2863 states (`Up/Down/Testing/Unknown/Dormant/NotPresent/
LowerLayerDown`); `AdminStatus DOWN` is an administrative state, never interpreted as a device failure.

## Counter / rate calculation

`IInterfaceRateCalculator.Calculate(previous, current)` computes bits-per-second and (when speed is known)
utilization. It never produces a negative or fabricated rate:

- `current < previous` → `CounterReset` (device reboot / interface reset / counter rollover) → `Unavailable`, wait
  for the next valid sample.
- zero/negative elapsed time → `Unavailable`.
- missing speed → utilization `null` ("unavailable").

## Monitoring & alert integration

- New `MonitorCheckType.SnmpTelemetry` + `SnmpCheckConfig` on `MonitoringCheck`. The executor dispatches SNMP checks
  to the collector (a separate path from the tool pipeline); the whole collection is bounded by the check timeout.
- `SnmpCollectionStatus` maps deterministically to a `MonitoringResult`: `Success → Healthy`, `Partial → Warning`,
  `Timeout → Timeout`, `Unavailable/Failed → Unhealthy (SnmpTelemetry)`. This flows through `HealthEvaluator` and
  `AlertEngine` exactly like the existing checks — an SNMP timeout becomes a structured monitoring failure and can
  raise an alert; interface status/counters are evidence, not automatic alerts.

## Persistence & retention

Schema v2 (additive migration) adds `snmp_device_telemetry` and `snmp_interface_telemetry`. Counters are stored as
exact decimal `TEXT` (Counter64 can exceed SQLite's signed 64-bit INTEGER); speed is `INTEGER`. Retention reuses the
Step 12 service: device snapshots 90 days, interface counters 30 days (configurable, active data preserved). The
collector persists best-effort — a persistence failure never fails a telemetry check. No secrets are persisted.

## AI integration (read-only)

- `network_device_telemetry` — latest device telemetry (sysName, description, uptime, reachability).
- `network_interface_telemetry` — latest interface telemetry (status, counters, errors).

Both depend solely on `ISnmpTelemetryRepository`; they cannot GET/WALK/SET a device themselves (asserted by tests).
The AI can answer "what is the uptime of SW01?", "which interfaces are down?", "show interface errors", "what is
the traffic on Gi1/0/1?" from structured, secret-free evidence.

## Discovery & topology integration

Out of scope this step. The fork does not yet carry discovery/topology modules (`NETWORK_DISCOVERY.md` /
`NETWORK_TOPOLOGY.md` do not exist). When they land, SNMP probing must respect explicit authorization, configured
scope/credentials, and rate limits — it must never probe every discovered IP automatically, and SNMP-derived
identifiers must be labeled as evidence (not authoritative over LLDP/CDP).

## Security review

- SNMP SET is impossible through this implementation (`ISnmpProvider` has no SET method — asserted by tests).
- Credentials use DPAPI secure storage; community strings/SNMPv3 passwords are never logged, never persisted in
  config, never in telemetry, never in AI context (asserted by tests).
- Endpoints are validated (host required; port 1–65535; version/credential match enforced); timeouts and retries are
  bounded; concurrency is bounded by the monitoring engine's existing `SemaphoreSlim`; monitoring scope is respected
  (only configured targets/checks are probed).
- Standard vs vendor-specific OIDs are kept separate: only MIB-II / IF-MIB are implemented; no generic CPU/memory
  OIDs are fabricated (those are vendor-specific and out of scope).

## Limitations

- No SNMP SET, configuration backup, or remediation (out of scope by design).
- No vendor-specific CPU/memory telemetry framework.
- No SNMP traps/informs.
- Rate calculation is per-sample; no aggregation/trending yet.
- The credential editor uses plain text input fields (masking is a follow-up refinement); stored secrets are still
  DPAPI-encrypted and never displayed.
- Discovery/topology enrichment deferred until those modules exist.

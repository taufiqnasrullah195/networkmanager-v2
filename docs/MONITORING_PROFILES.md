# TheWiseNetwork — Monitoring Profiles (Step 10)

STEP 10 moves monitoring configuration out of hand-edited JSON into the WPF application. The user manages
profiles, targets, and checks from the UI; the AI remains strictly read-only.

## Golden rule

**The editor manages configuration. The engine executes monitoring. The evaluator interprets measurements. The
AI reads evidence but cannot modify configuration.** These stay separate so the next steps (alerting, persistent
state, SNMP) can land without rewriting this layer.

## Architecture

```
WPF (NetworkMonitoringView / NetworkMonitoringViewModel)
        │
        ▼
IMonitoringProfileService  (CRUD + validation, in-memory over a repository)
        │
        ▼
IMonitoringProfileRepository → JsonMonitoringProfileRepository (checksummed, versioned catalog)
        │
        ▼
MonitoringProfileCatalog / MonitoringProfile  (configuration — persisted)
        │
        ▼  (applied while stopped, via MonitoringConfigurationApplier)
IMonitoringEngine  →  scheduler / checks / health  (runtime — in-memory)
```

Configuration (profiles/targets/checks/interval/timeout/enabled) is **separate from runtime state** (health,
last check, last result, last error, transitions). The editor modifies configuration; it never touches runtime
state directly — it applies a profile set to the engine through `MonitoringConfigurationApplier`, and only while
monitoring is stopped.

## Models

- **`MonitoringProfile`** — `Id` (stable; falls back to `Name` for legacy), `Name`, `Description`, `Enabled`,
  `Targets`, `Checks`, `DefaultInterval`, `DefaultTimeout`, `MaxConcurrency` (reserved). See
  `docs/NETWORK_MONITORING.md` for the Step 9 target/check shapes.
- **`MonitoringProfileCatalog`** — versioned list of profiles persisted together.

## Service (`MonitoringProfileService`)

`IMonitoringProfileService` owns configuration: `GetProfilesAsync`, `GetProfileAsync`, `CreateProfileAsync`,
`UpdateProfileAsync`, `DeleteProfileAsync`, `EnableProfileAsync`, `DisableProfileAsync`, `ValidateProfile`, and
`LastError`. Every mutation validates first, enforces unique profile names/ids, and persists atomically.
Malformed/tampered configuration is handled safely (reported via `LastError`, never a crash).

## Editor behavior

- **Draft + Save/Cancel**: the editor works on a `MonitoringProfileDraft`; edits do not reach the service until
  Save. Cancel discards the draft. Unsaved changes prompt "Save before continuing?" (Save / Discard / Cancel)
  before selecting another profile, deleting, or starting.
- **Targets**: add/remove/enable, with `Id`, `Name`, hostname-or-IP, `Type`, `Description`. The address maps to
  `IPAddress` when it parses as an IP, otherwise `Hostname`.
- **Checks**: PING / TCP_CONNECTIVITY / DNS_RESOLUTION, with type-specific fields (`Port` for TCP), optional
  timeout/interval, `Enabled`, and a target reference. The check probes its **target's** address — there is no
  separate per-check host field (the underlying executor uses the target address).
- **Global concurrency**: one shared engine runs all enabled profiles, so `MaxConcurrency` is global (default 5);
  it is applied by rebuilding the engine while stopped.

## Validation (multi-level, user-facing)

- Profile: name required + unique, interval/timeout in range (interval ≥ 1 s, timeout ≥ 100 ms), concurrency
  1–64, an enabled profile must contain at least one target, duplicate target/check ids rejected, duplicate
  target addresses warn.
- Target: identifier + address (hostname or IP) required.
- Check: supported type, target reference, TCP port 1–65535, timeout/interval in range.

Messages are clear ("Interval must be between 1 second and 24 hours.", "TCP port must be between 1 and 65535.").
Minimum interval is enforced and documented here: 1 second.

## Persistence & backward compatibility

`JsonMonitoringProfileRepository` stores a checksummed (SHA-256), versioned catalog at
`%LocalAppData%\NETworkManager\AI\monitoring-profiles.json`. On first load it **migrates** the Step 9 single-profile
file (`monitoring-profile.json`) into the catalog — existing configuration is not lost. Missing fields get
defaults (System.Text.Json initializers); malformed/tampered data is rejected cleanly, not loaded half-broken. The
UI never knows the storage is JSON (repository abstraction).

## Start/Stop & apply behavior

- **Start**: rebuilds the engine (with the current global concurrency), applies the enabled profiles
  (targets then checks; per-check interval/timeout resolved from profile defaults), then starts the loop. No
  duplicate loops (start is idempotent), no orphaned tasks.
- **Stop**: cancels and awaits the loop; in-flight checks finish as `Cancelled`.
- **Apply while running**: not supported (no unsafe hot-reload). Changing configuration persists on Save; it takes
  effect on the next Start. The view stops the engine gracefully on unload.
- Runtime status (Running/Stopped, active targets, healthy/degraded/unhealthy counts, per-target table) always
  comes from the actual engine state — never computed in the UI.

## Security

Profiles contain no credentials (no passwords/keys/tokens; future checks must reference a secure credential id).
The repository is checksummed to detect tampering. The configuration write path is UI-only — the AI has no route
into it.

## AI boundary

The AI Copilot keeps only `network_monitoring_status` (read-only, `IMonitoringQuery`-only). It cannot create/edit/
delete profiles or targets/checks, change intervals, or start/stop monitoring. `MonitoringProfileService` does not
implement `INetworkTool`, so it can never be registered into a tool registry (asserted by tests).

## Known limitations

- Global concurrency is not persisted across app restarts (per-profile interval/timeout are).
- Hot-reload (apply while running) is intentionally not implemented.
- The editor is verified by build/test on Windows CI; manual visual verification needs a Windows machine.

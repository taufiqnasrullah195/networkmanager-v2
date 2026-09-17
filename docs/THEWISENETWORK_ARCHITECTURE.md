# TheWiseNetwork — Architecture

This document describes the **current** architecture of the codebase (which is NETworkManager's architecture, preserved) and defines **where** the future TheWiseNetwork functionality will live. Anything not yet implemented is explicitly marked **Planned**.

---

## 1. Existing NETworkManager architecture

| Aspect | Detail |
|---|---|
| Language / runtime | C# / .NET 10 (SDK pinned in `Source/global.json`) |
| UI | WPF + MahApps.Metro, MVVM (manual DI — no container) |
| Target | `net10.0-windows10.0.22621.0` (Windows 11, x64/arm64) |
| Size | ~720 C# files, 110 XAML views, 113 ViewModels, 12 projects |

### Solution layout (`Source/NETworkManager.sln`)

```
Source/
├── NETworkManager/               # Main WPF app: Views, ViewModels, Controls, App.xaml
├── NETworkManager.Models/        # Business logic + network engines (Ping, IPScanner, DNSLookup, ...)
├── NETworkManager.Utilities/     # Cross-cutting helpers (AssemblyManager lives in .Settings)
├── NETworkManager.Utilities.WPF/ # WPF-specific utilities
├── NETworkManager.Converters/    # WPF value converters
├── NETworkManager.Validators/    # Input validation
├── NETworkManager.Controls/      # Custom WPF controls (RDP, PuTTY, PowerShell, TigerVNC hosts)
├── NETworkManager.Localization/  # 17-language RESX strings
├── NETworkManager.Settings/      # Settings model + persistence, AssemblyManager, PolicyManager
├── NETworkManager.Profiles/      # (Encryptable) host/network profiles
├── NETworkManager.Update/        # GitHub-based update check (Octokit)
├── NETworkManager.Documentation/ # Library/ops documentation data
├── NETworkManager.Setup/         # WiX MSI installer
└── 3rdparty/                     # Dragablz submodule — DO NOT modify (maintained manually)
```

---

## 2. Application entry point

`NETworkManager.App` (`Source/NETworkManager/App.xaml` + `App.xaml.cs`):

1. Parse command line (`CommandLineManager`).
2. `PolicyManager.Load()` — system-wide policies.
3. `LocalSettingsManager.Load()` / `SettingsManager.Load()`.
4. `SettingsManager.Upgrade()` if settings version < current version.
5. `LocalizationManager` — load language, set `Strings.Culture`.
6. Single-instance mutex, background job timer, splash screen.
7. `StartupUri` → `MainWindow.xaml`.

Assembly identity/version comes from `AssemblyManager` (reads `Assembly.GetEntryAssembly()`), which also determines the **Name** used for all on-disk paths (see §6).

---

## 3. UI architecture

- **MVVM**: Views in `Source/NETworkManager/Views/*.xaml`, ViewModels in `ViewModels/`, code-behind kept minimal.
- **Base classes**: `ViewModelBase`, `PropertyChangedBase`, `RelayCommand` (in `Utilities`).
- **Theming**: MahApps.Metro resource dictionaries merged in `App.xaml`; light/dark + accent switching.
- **Window host**: `MainWindow` is a MahApps `MetroWindow` embedding application views via a `Dragablz` tab control.
- **Embedded native tools** (`NETworkManager.Controls`): RDP (AxMSTSCLib), PuTTY/PowerShell/TigerVNC — all Windows-native via `WindowsFormsHost`.

---

## 4. Core services

| Service | Location | Responsibility |
|---|---|---|
| `SettingsManager` | `NETworkManager.Settings` | Load/save XML app settings, version migration |
| `LocalSettingsManager` | `NETworkManager.Settings` | Per-user local settings (window placement, etc.) |
| `ProfileManager` | `NETworkManager.Profiles` | Host/network profiles (XML, optional AES encryption) |
| `AssemblyManager` | `NETworkManager.Settings` | Entry-assembly name/version/location |
| `PolicyManager` | `NETworkManager.Settings` | System-wide policies (enterprise lockdown) |
| `LocalizationManager` | `NETworkManager.Localization` | Language/culture resolution |
| `Updater` | `NETworkManager.Update` | Checks GitHub releases (upstream account) |

---

## 5. Network functionality

Located in `Source/NETworkManager.Models/Network/` — these are the engines the future AI layer will wrap, unchanged:

- Reachability: `Ping`, `Traceroute`
- Resolution: `DNSLookup`, `DNSServer`
- Scanning: `IPScanner`, `PortScanner`, `PortProbe`, `HostRangeHelper`
- Topology: `DiscoveryProtocol` (LLDP/CDP), `NeighborTable` (ARP/NDP), `SNMPClient`
- Interfaces: `NetworkInterface`, `NetworkInterfaceConfig`, `NetworkProfiles`
- Other: `Firewall`, `HostsFileEditor`, `BandwidthMeter`, `WakeOnLAN`, `SubnetCalculator`

---

## 6. Configuration & profile handling

All on-disk paths derive from `AssemblyManager.Current.Name` (the **assembly name**, currently `NETworkManager` — intentionally kept for backward compatibility):

- Settings: `Documents\{Name}\Settings\`
- Local settings: `LocalApplicationData\{Name}\`
- PuTTY logs / WebConsole cache: `LocalApplicationData\{Name}\...`
- Profiles: `Documents\{Name}\...` (via `ProfileManager`)

> **Compatibility invariant:** `AssemblyManager.Current.Name` (`AssemblyName` in `NETworkManager.csproj`) is **not renamed** to TheWiseNetwork. Changing it would orphan existing users' settings, encrypted profiles, and logs. Product branding is applied only to user-facing text (window/About title, assembly `Title`/`Product` metadata).

---

## 7. Existing extension points

- **New .NET projects** can be added to `Source/NETworkManager.sln` as siblings and referenced by the main app (upstream already splits logic into 11 class libraries — a clean seam).
- **MVVM**: new capabilities plug in as View + ViewModel + registration in `MainWindow`.
- **Models layer**: network engines are plain classes; they can be invoked outside the UI for tool-based access.
- **Settings upgrade** (`SettingsManager.Upgrade`) provides a versioned migration path.
- **`IAIProvider`-style seams do not yet exist** — that abstraction is introduced in Phase 2 (Planned).

---

## 8. Where future TheWiseNetwork functionality will live (Planned)

New sibling projects under `Source/` (see `docs/TECHNICAL-REQUIREMENTS.md` §5 and `docs/ROADMAP.md`). The AI provider/tool foundation exists (Steps 3–5); the rest are planned:

```
TheWiseNetwork (product)
└── Source/
    ├── NETworkManager.*          (existing NETworkManager — unmodified where possible)
    └── NETworkManager.AI         (implemented)  provider layer + tool registry + execution + orchestration (Steps 3–5)
        NETworkManager.AI.Tools   (implemented)  typed tools wrapping Models engines
        NETworkManager.AI.Tests   (implemented)  xUnit tests (cross-platform)
        NETworkManager.Policy     (Planned)  risk classification + permission
        NETworkManager.CommandExecution (Planned)  controlled PowerShell/device execution
        NETworkManager.Audit      (Planned)  audit records
        NETworkManager.Knowledge  (Planned)  knowledge base / RAG
        NETworkManager.Agents     (Planned, later)  agent hosts
```

`Source/3rdparty/` remains off-limits.

---

## 9. AI integration boundary

```
User → TheWiseNetwork UI
        → AI copilot (tool selection)
        → Policy engine (risk classification)
        → Permission check → Approval (if required)
        → Execution (wrapped diagnostic engine / command layer)
        → Result → AI analysis (evidence-labelled) → Audit
```

Rules: the AI is given a bounded typed tool list; **no unrestricted command execution**; every execution is audited; the AI must not invent tool results.

### 9.1 AI provider layer (implemented)

AI backends are reached only through `IAIProvider` (see `docs/AI_PROVIDERS.md` and
`docs/CUSTOM_AGENT_PROTOCOL.md`):

```
                   TheWiseNetwork
                          |
                   AI Provider Layer (IAIProvider)
                          |
            +-------------+-------------+
            |             |             |
       Future API      Local AI    Custom Agent
       Providers       Provider    Provider (implemented)
       (NOT IMPLEMENTED)(NOT IMPLEMENTED) |
                                         |
                                         v
                                   External Agent
                                         |
                                         v
                                    Tool Request
                                         |
                                         v
                                    Tool Registry
                                         |
                                         v
                                 Policy / Validation
                                         |
                                         v
                                 Tool Execution
                                         |
                                         v
                                 Network Services
```

Status:

- `CustomAgentProvider` — **implemented** (HTTP protocol; agent requests tools, never executes them).
- `MockAIProvider` — **implemented** (test/double provider).
- Future API providers (OpenAI, Anthropic, OpenRouter, …) — **NOT IMPLEMENTED** (no SDK referenced).
- Local AI provider — **NOT IMPLEMENTED**.

### 9.2 Tool call orchestration (implemented)

STEP 5 connects the provider layer to the typed tools through a controlled orchestration boundary
(see `docs/AI_TOOL_ORCHESTRATION.md`):

```
Custom Agent
  |
  v
AI Tool Call
  |
  v
Tool Call Orchestrator
  |
  +--> Tool Registry
  |
  +--> Policy
  |
  +--> Approval
  |
  v
Tool Execution
  |
  v
Structured Result
  |
  v
Custom Agent
```

Status:

- `ToolCallOrchestrator` — **implemented** (resolve → validate → policy → approval → execute → structured outcome).
- `DefaultToolPolicyService` — **implemented** (LOW allow, MEDIUM/HIGH require approval, CRITICAL deny).
- `DefaultToolApprovalService` — **implemented** (approval read from context; interactive UI later).
- `AgentToolLoop` — **implemented** (bounded multi-round loop with maximum-rounds/repeated-call protection).

The AI reasons; TheWiseNetwork validates, authorises, and executes; the tools observe; the evidence returns to the AI.

### 9.3 Diagnostic Engine (implemented)

STEP 6 layers a deterministic, read-only diagnostic engine underneath the orchestration boundary
(see `docs/DIAGNOSTIC_ENGINE.md`):

```
AI / Custom Agent
  |
  v
Tool Orchestrator
  |
  v
Diagnostic Engine
  |
  v
Diagnostic Tools
  |
  v
Network / OS
```

Status:

- `DiagnosticEngine` — **implemented** (runs a `DiagnosticWorkflow` through the tool execution service, collects ordered evidence).
- `DiagnosticWorkflow` / `DiagnosticStep` — **implemented** (reusable, dependency-aware step definitions).
- `DefaultDiagnosticAnalyzer` — **implemented** (deterministic evidence → failure classification + recommendation).
- `InternetConnectivityDiagnostic` — **implemented** (8-step read-only workflow: adapter → IP → route → gateway → DNS → external → TCP → traceroute).
- `InternetConnectivityDiagnosticTool` — **implemented** (exposes the workflow as one `LOW`-risk registered tool).

### 9.4 AI Conversation & evidence-based reasoning (implemented)

STEP 7 adds the first real interaction layer: an evidence-aware conversation service (see `docs/AI_CONVERSATION.md`):

```
User
  |
  v
AI Conversation Service
  |
  v
AI Provider
  |
  v
Tool Call
  |
  v
Tool Orchestrator
  |
  v
Diagnostic Engine
  |
  v
Evidence
  |
  v
AI Provider
  |
  v
Response
```

Status:

- `IAIConversationService` / `AIConversationService` — **implemented** (in-memory conversation → provider loop → structured response).
- `AIConversation` / `AIMessage` / `AIAnalysisResponse` / `AIFinding` / `AIEvidenceContext` — **implemented** (provider-neutral).
- `NetworkDiagnosticInstructions` — **implemented** (controlled system prompt: evidence-first, no fabrication, restricted access).
- `DefaultSensitiveDataFilter` — **implemented** (redacts secrets before sending to the provider).
- Reuses `IAgentToolLoop` (STEP 5) and `IDiagnosticAnalyzer`/`DiagnosticEngine` (STEP 6); no new loop-limit system.

### 9.5 AI Copilot UI (implemented)

STEP 8 binds the conversation layer to a minimal WPF chat surface (see `docs/AI_COPILOT.md`):

```
User
  |
  v
AI Conversation Service
  |
  v
AI Provider
  |
  v
Tool Call
  |
  v
Tool Orchestrator
  |
  v
Diagnostic Engine
  |
  v
Evidence
  |
  v
AI Provider
  |
  v
Response
```

Status:

- `AICopilotView` / `AICopilotViewModel` / `CopilotController` / `AICopilotFactory` — **implemented** (chat input, loading, cancellation, live tool activity, structured findings + evidence).
- Registered as a standard application (`ApplicationName.AICopilot`) in the existing navigation; no UI redesign.
- Runtime provider integration — **implemented**: `CopilotProviderConfiguration` (JSON, checksummed, no secrets) + `DpapiSecureCredentialStore` (Windows DPAPI) + `ProviderRegistry` selection (Custom Agent when configured, mock otherwise).
- `ToolActivityNotifier` — **implemented** (Step 5/6 structured logging projected into live `ToolActivity` events for the UI; no signature changes).
- HTTPS required (HTTP only via explicit development opt-in); TLS validation never disabled.

---

### 9.6 Network Monitoring & Health Engine (implemented)

STEP 9 adds continuous, read-only observation (see `docs/NETWORK_MONITORING.md`):

```
WPF (NetworkMonitoringView)                  AI Copilot (network_monitoring_status)
        │                                                    │
        ▼                                                    ▼
 NetworkMonitoringViewModel ──► IMonitoringEngine ◄── IMonitoringQuery
                                    │
                     ┌──────────────┴───────────────┐
                     ▼                              ▼
           MonitoringScheduler               MonitoringStateStore
                     │
                     ▼
            MonitoringCheckExecutor ──► ping / tcp_test / dns_lookup (Step 3 tools)
                     │
                     ▼
              MonitoringResult ──► HealthEvaluator ──► NetworkHealthStatus ──► MonitoringEvent
```

Status:

- `MonitoringTarget`/`MonitoringCheck`/`MonitoringResult`/`MonitoringEvidence`/`NetworkHealthStatus` — **implemented**.
- `MonitoringEngine` + `MonitoringScheduler` — **implemented** (one loop, `SemaphoreSlim`-bounded concurrency, per-check timeouts, cancellation, graceful stop, idempotent start/stop).
- `HealthEvaluator` — **implemented** (deterministic; timeout/unreachable/DNS/TCP kept distinct; never infers "down" from missing/blocked ICMP).
- `IMonitoringStateStore` (in-memory) + `IMonitoringObserver` events (`HealthStateChanged` only on real transitions) — **implemented**, persistence-ready.
- `MonitoringProfile`/`MonitoringProfileStore` — **implemented** (JSON, checksummed, no secrets; targets externalized).
- `network_monitoring_status` (LOW/read-only, depends on `IMonitoringQuery` only) — **implemented**; registered in the copilot registry.
- `NetworkMonitoringView`/`ViewModel` — **implemented** (minimal status table; engine lifecycle view-scoped this step).
- Reuses the upstream `Ping`/`DNSLookup` engines via the Step 3 wrappers (no duplicate networking); upstream WPF Ping Monitor untouched.

STEP 10 (see `docs/MONITORING_PROFILES.md`) adds configuration management over this engine:

- `IMonitoringProfileService` + `JsonMonitoringProfileRepository` (checksummed, versioned catalog; migrates the Step 9 single-profile file).
- `MonitoringConfigurationApplier` (apply enabled profiles to the engine while stopped; resolves per-check interval/timeout).
- Profile/target/check validation, `MonitoringProfileDraft` save/cancel, global Start/Stop commands, and the full editor in `NetworkMonitoringView`.
- AI stays read-only: configuration writes are UI-only; `network_monitoring_status` gains no mutation surface.

---

### 9.7 Alert Engine & Health State Notifications (implemented)

STEP 11 (see `docs/ALERT_ENGINE.md`) turns monitoring state changes into structured alerts:

```
MonitoringEngine ──► HealthStateChanged ──► AlertEngine ──► AlertEvaluator ──► AlertStore ──► Alerts/AlertEvent
                                                                                             │
                                                                              ┌──────────────┼──────────────┐
                                                                              ▼              ▼              ▼
                                                                             WPF      Future notifier      AI (network_alerts)
```

- `Alert`/`AlertEvaluator`/`AlertStore`/`AlertEngine`/`AlertOptions` — **implemented** (deterministic severity, dedup by `TargetId+CheckType`, occurrence tracking, `Open→Acknowledged→Resolved`, evidence-driven resolution).
- `IAlertObserver` events + no-op `IAlertSuppressionPolicy` boundary — **implemented**.
- `network_alerts` (LOW, read-only, `IAlertQuery`-only) — **implemented**; registered in the copilot registry.
- WPF alerts panel (list + detail + acknowledge) in `NetworkMonitoringView`; `MonitoringEvent.Result` enriched to feed occurrence tracking.

---

## 10. Agent execution boundary (Planned)

Future agents (discovery, troubleshooting, monitoring, security, configuration, documentation) execute only through the same policy-gated tool/command layer. No agent runs arbitrary code. Multi-agent architecture is **not** implemented prematurely.

---

## 11. Security boundary

- Read-only diagnostics: LOW risk (policy may auto-execute).
- Mutating actions: MEDIUM (approval normally required) to HIGH (explicit approval).
- Destructive actions: CRITICAL (blocked by default).
- Secrets: never hard-coded; config uses placeholders; stored in platform secure storage; never logged.

---

## 12. Future automation boundary (Planned)

Automation (PowerShell agent, network device agents, controlled command execution, approval workflow) is Phase 4. It is separated from the read-only diagnostic layer and always passes through risk classification + human approval.

---

## Diagrams

```
User
  |
  v
TheWiseNetwork UI (WPF, MainWindow)
  |
  v
Application / Core services
  +------------------------------+
  |                              |
  |                              |
Existing network                Future TheWiseNetwork
functionality                   extensions (Planned)
(NETworkManager.Models          |
 engines, untouched)            +----------+----------+----------+
                                |          |          |          |
                               AI        Policy    Audit     Knowledge
                                |
                         Policy Layer
                                |
                         Execution Layer
```

Boundaries marked *(Planned)* do not exist in the current codebase and must not be described as implemented.
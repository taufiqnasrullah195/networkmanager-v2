# TheWiseNetwork — AI Copilot (Step 8)

STEP 8 connects the full AI stack — provider layer, conversation service, tool orchestration, diagnostics — to a
**minimal, production-oriented WPF copilot UI**, and wires the **real `CustomAgentProvider` through runtime
configuration with secure credential storage**. UI integration + runtime provider integration only: no redesign, no
new business logic.

## Architecture

```
WPF View (AICopilotView)
  |
  v
CopilotViewModel (MVVM; projection + dispatcher marshalling only)
  |
  v
CopilotController (UI-agnostic, testable)
  |
  v
IAIConversationService (Step 7)
  |
  ├──► IAIProvider  ──► CustomAgentProvider ──► External Custom Agent
  |                 └──► MockAIProvider (development placeholder)
  |
  └──► IToolCallOrchestrator ──► Tool Registry ──► Network Tools / Diagnostic Engine
                                     |
                                     v
                              Structured Evidence ──► AIAnalysisResponse ──► ViewModel ──► UI
```

Credential flow (separate, never through the UI):

```
CustomAgentProvider (per request)
  |
  v
ISecureCredentialStore ──► DpapiSecureCredentialStore (Windows DPAPI, CurrentUser)
```

## UI

`AICopilotView` (registered as `ApplicationName.AICopilot`):
- **Header** — title, subtitle, provider indicator (`Provider: Custom Agent` / `Mock (development)`). Never endpoint
  secrets, tokens, or headers.
- **Chat area** — user/assistant entries. Assistant entries carry structured findings (type + text) and structured
  evidence lines (✓/✗/⚠ + title + provenance detail).
- **Tool activity panel** — live lines `● tool` → `✓ tool (12 ms)` / `✗ tool — errorCode`, from real tool metadata.
- **Input area** — single-line input + Send + Cancel. Send disabled when busy or empty; Cancel enabled only while busy.
- **Status/loading** — indeterminate `MetroProgressBar` + status text (`Processing request...`, `Running dns_lookup...`,
  `Completed`, `Request cancelled.`).

ViewModel responsibilities: project `AIConversationResult` into bindable collections, marshal background
`ToolActivity`/status events onto the dispatcher, keep `IsBusy` consistent. All orchestration lives in
`CopilotController` (no WPF dependency — covered by unit tests).

## Custom Agent runtime configuration

- `CopilotProviderConfiguration` — persisted via `FileCopilotConfigurationStore`
  (`%LocalAppData%\NETworkManager\AI\copilot-provider.json`, checksum-verified): `Enabled`, `Endpoint`,
  `AuthenticationMode` (none/api-key/bearer), `AllowHttpForDevelopment`, `TimeoutSeconds`, `CredentialKey`.
  **No secrets in the file** — only the credential *key reference*.
- `DpapiSecureCredentialStore` (`%LocalAppData%\NETworkManager\AI\credentials\`) — DPAPI `CurrentUser`-scope
  encryption via `System.Security.Cryptography.ProtectedData`. UI only ever knows *whether* a credential exists.
- Provider selection flows through `ProviderRegistry` (Step 4): configured Custom Agent when valid + credential
  present, mock otherwise. Future providers plug in without touching the copilot UI.
- Transport: **HTTPS required**; plain HTTP only with the explicit, documented `AllowHttpForDevelopment` opt-in.
  TLS validation is never disabled; the provider uses the default `HttpClientHandler`.

## Tool activity

`ToolActivityNotifier` implements both `IToolOrchestrationLogger` (Step 5) and `IDiagnosticLogger` (Step 6), so it
plugs into the existing pipeline **without changing any execution signature**. It projects structured log events into
`ToolActivity` records (tool name, category, step, status, duration, safe summary) and raises them as .NET events.
The VM marshals them to the dispatcher. Only known-safe fields are projected — even a payload containing
secret-like keys cannot leak into the UI (covered by a unit test).

## Evidence

`CopilotEvidenceProjection` projects **only** structured data: `AIEvidenceContext.Evidence` (per-diagnostic-run) or
`AIFinding` of type `Observation` (single-tool responses). Inference/recommendation findings are never rendered as
evidence, and a text-only response produces **zero** evidence items no matter what the text claims (tested).

## Cancellation

`Cancel` → `CancellationTokenSource.Cancel()` → conversation service → provider request (linked timeout) →
orchestrator (pre-dispatch gate + `ToolCallCancelled` event) → diagnostic engine (workflow `CancelAfter`). The VM
clears `IsBusy` in a `finally` — the UI can never get stuck in loading. Cancellation is a normal result
("Request cancelled."), never a crash.

## Error handling

Provider unavailable / timeout / auth-failure / invalid response map to friendly messages in
`AIConversationService` + `CopilotController` (`"The AI provider timed out."`, `"Authentication with the AI provider
failed. Check the configured credential."`, ...). Raw exception details go to log4net only. Sanitization is
tested (no IP addresses / stack fragments leak into UI status text).

## Approval

The copilot goes through `ToolCallOrchestrator` like everyone else: MEDIUM/HIGH → `ApprovalRequired` (nothing
executes), CRITICAL → `PolicyDenied`. There is **no approval bypass path**; the interactive approval dialog is a
future step — until then, medium/high tools simply don't run from the copilot (tested).

## Security model

- No secrets in source, config JSON, ViewModels, conversation history, `ToolResult`, `DiagnosticReport`, or logs.
- Secrets: DPAPI-encrypted file store, referenced by key; UI shows existence only.
- HTTPS enforced (HTTP only via explicit dev opt-in); TLS validation never disabled.
- All tool execution stays behind the registry/policy/approval/loop-limit gates (Steps 3–7).

## Testing strategy

`NETworkManager.AI.Tests` (cross-platform, no network):
- **CopilotControllerTests** — busy-state transitions, empty/second-message rejection, cancellation (no stuck UI),
  provider/timeout/auth error mapping, live activity events (started/completed/failed from real pipeline),
  evidence projection, approval enforcement.
- **ToolActivityNotifierTests** — event mapping, diagnostic events, no-secret-leak guarantee.
- **CopilotProviderConfigurationTests** — HTTPS/HTTP-dev validation, endpoint validation, JSON roundtrip, no
  secrets in JSON.
- **SecureCredentialStoreTests / CopilotEvidenceProjectionTests** — store contract, text-cannot-fabricate-evidence.
- **CustomAgentRuntimeIntegrationTests** — credential-store-backed api-key/bearer auth flows through the real
  `CustomAgentProvider` + `MockCustomAgentHandler`, provider registry selection, timeout.

WPF XAML/compile verification runs on Windows CI (full solution build). Manual visual verification still needs a
Windows machine (not available on this host).
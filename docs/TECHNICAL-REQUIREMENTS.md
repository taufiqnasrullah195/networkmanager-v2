# TheWiseNetwork — Technical Requirements & Architecture

> Repo: `taufiqnasrullah195/networkmanager-v2`
> Product: **TheWiseNetwork** — an AI-powered network administration, diagnostics, troubleshooting, monitoring, and automation platform built on top of the upstream [NETworkManager](https://github.com/BornToBeRoot/NETworkManager) project.
> License: **GPL-3.0** (fork remains open source — accepted decision D-1).
> Status: Draft v0.1 — Step 1 establishes the repository and the architectural contract. No upstream code is modified yet.

---

## 1. Project identity

TheWiseNetwork preserves the stability, trust, and feature set of NETworkManager while adding a *controlled* AI layer, network automation, documentation, and safe operations. NETworkManager v2 in spirit; **TheWiseNetwork** in brand.

Core philosophy (unchanged from the project rules):

1. Stability before features.
2. Security before convenience.
3. Evidence before AI conclusions.
4. Human approval before risky actions.
5. Modular architecture before quick hacks.
6. Backward compatibility wherever reasonably possible.

---

## 2. Upstream baseline (measured, not assumed)

| Dimension | Value |
|---|---|
| Language / runtime | C# / **.NET 10** (SDK pinned `10.0.400` via `Source/global.json`) |
| UI framework | **WPF** with MahApps.Metro (Windows-only) |
| Target framework | `net10.0-windows10.0.22621.0` (x64, Windows 11) |
| Source size | ~97,000 lines of C#, 720 `.cs` files, 110 XAML views, 113 ViewModels |
| Projects | 12 (`NETworkManager`, `.Models`, `.Utilities`, `.Utilities.WPF`, `.Converters`, `.Validators`, `.Controls`, `.Localization`, `.Settings`, `.Profiles`, `.Update`, `.Documentation`) plus `.Setup` (WiX MSI) |
| Upstream tests | **None** — `Tests/` is empty; upstream relies on manual testing (see §16 for how TheWiseNetwork corrects this) |
| License | GPL v3 |

### Where the network tools actually live

`Source/NETworkManager.Models/Network/` contains the diagnostic engines the AI layer will wrap:

- **Reachability**: `Ping`, `Traceroute` (via `MaximumHopsReachedArgs`, `PingReceivedArgs`)
- **Resolution**: `DNSLookup`, `DNSServer`, `HostnameArgs`
- **Scanning**: `IPScanner`, `PortScanner`, `PortProbe`, `HostRangeHelper`
- **Topology**: `DiscoveryProtocol` (LLDP/CDP), `NeighborTable`, `SNMPClient`
- **Interfaces**: `NetworkInterface`, `NetworkInterfaceConfig`, `NetworkProfiles`, `IPConfigReleaseRenewMode`
- **Firewall/Hosts**: `Firewall`, `HostsFileEditor`
- **Remote access (controls)**: RDP, PuTTY (SSH/Telnet/Serial), PowerShell, TigerVNC — in `Source/NETworkManager/Controls/`

The extension strategy is to **wrap** these engines behind typed AI tools, never to fork their internals.

---

## 3. Licensing (decision D-1)

TheWiseNetwork is a derivative of a GPL-3.0 work. The fork and **all extensions it links into** are GPL-3.0.

Consequences, binding on every future step:

- Any code in this repository that is linked with the upstream solution is GPL-3.0.
- AI providers remain **external services** reached over HTTPS with API keys; provider SDKs/HTTP clients are compatible with GPL (no key ever committed).
- If a future requirement demands a proprietary component, it must live in a **separate repository/process** that communicates with TheWiseNetwork only over a documented API — and that decision requires a new explicit approval. It is out of scope for Step 1.

---

## 4. Architecture principles (normative)

Derived from the project rules; violations block merge.

- **Modularity.** No giant classes/methods; no duplicated business logic; no global mutable state; no tight UI↔business coupling.
- **AI is not a shell.** The AI layer operates only through typed, policy-gated tools (§6–§8).
- **Interface-first.** Prefer interfaces, DI (manual — upstream has no DI container), services, providers, adapters, repositories, strongly-typed models.
- **Evidence discipline.** AI output separates *observed facts*, *calculated results*, *inferences*, *recommendations*, and *uncertain assumptions* (§13). Never up-rank an inference to a fact.
- **Backward compatibility.** Existing NETworkManager functionality keeps working; extensions are additive and isolated.

### Target dependency direction

```
UI (WPF — extends existing Views/ViewModels, adds an AI panel)
  │
Application Layer (business logic)
  ├── NETworkManager.Models  (existing diagnostic engines — UNCHANGED where possible)
  ├── NETworkManager.AI      (copilot, provider abstraction, tool orchestration)
  ├── NETworkManager.Policy  (risk classification + permission decision)
  ├── NETworkManager.Audit   (audit records)
  └── NETworkManager.Knowledge (future)
  │
Infrastructure
  ├── Windows / PowerShell   (command execution layer)
  ├── Network devices        (SSH / SNMP — future)
  └── External AI APIs       (OpenAI / Anthropic / OpenRouter / Local)
```

---

## 5. Extension boundary — new projects

To satisfy "extend, don't rewrite," TheWiseNetwork-specific code goes into **new sibling projects** under `Source/`. Submodule `Source/3rdparty/` is off-limits (upstream rule: maintained manually, AI MUST NOT modify).

Planned projects (added in later steps, not Step 1):

| Project | Responsibility |
|---|---|
| `NETworkManager.AI` | AI copilot engine; `IAIProvider` abstraction and provider adapters (OpenAI, Anthropic, OpenRouter, Local/OpenAI-compatible); conversation state; tool call orchestration |
| `NETworkManager.AI.Tools` | Typed tool registry (§7); each tool wraps an existing `NETworkManager.Models` engine; each carries name/description/typed input/typed output/permission level/timeout/logging |
| `NETworkManager.Policy` | Policy engine: risk classification table (§9), permission check, approval gating |
| `NETworkManager.CommandExecution` | Controlled execution of PowerShell / device commands (§8). No unrestricted command execution to AI, ever |
| `NETworkManager.Audit` | Audit record model + persistence (§12) |
| `NETworkManager.Knowledge` | Knowledge base / RAG (future — §14) |
| `NETworkManager.Agents` | Future agent hosts (discovery, troubleshooting, monitoring, security, config, documentation) — foundation only, see §15 |

The main `NETworkManager` WPF project gains only view/viewmodel glue (an AI panel, approval dialogs, audit viewer). No AI logic is embedded in unrelated UI components.

---

## 6. AI subsystem architecture

```
User
  → AI (copilot) — chooses a tool via tool-selection
  → Tool Selection (typed registry entry)
  → Policy Engine (classify risk)
  → Permission Check (user/role)
  → Approval if required (human-in-the-loop)
  → Execution (wrapped diagnostic engine / command layer)
  → Result (typed, captured)
  → AI analysis (rendered with evidence labels)
  → Audit (recorded)
```

Constraints:

- The AI is handed a **bounded tool list** per turn; it cannot call arbitrary code.
- Every path funnels through the policy engine — there is **no bypass**.
- Every execution is **audited** even on the "allow" path.
- The AI must not **invent tool results**: any tool output must come from an actual execution; the renderer never fabricates a result on timeout/error.

### Provider abstraction (rule #23)

```csharp
public interface IAIProvider
{
    string Name { get; }
    Task<AIResponse> ChatAsync(AIRequest request, CancellationToken ct);
    Task<AIResponse> ChatWithToolsAsync(AIRequest request, IReadOnlyList<AITool> tools, CancellationToken ct);
}
```

Providers: `OpenAIProvider`, `AnthropicProvider`, `OpenRouterProvider`, `LocalProvider` (OpenAI-compatible endpoint). Switching providers must not require rewiring the AI layer.

---

## 7. Tool registry spec

Each tool is a strongly-typed entry. Names below are indicative and map 1:1 to existing engines in `NETworkManager.Models`.

| Tool | Engine (existing) | Default risk |
|---|---|---|
| `ping` | `Ping` | LOW |
| `traceroute` | `Traceroute` | LOW |
| `dns_lookup` | `DNSLookup` | LOW |
| `port_test` | `PortProbe` / `PortScanner` (single target) | LOW |
| `ip_scan` | `IPScanner` (bounded range) | LOW |
| `network_adapters` | `NetworkInterface` | LOW |
| `routing_table` | via PowerShell `Get-NetRoute` | LOW |
| `arp_neighbors` | `NeighborTable` | LOW |
| `dhcp_info` | via PowerShell `Get-NetIPConfiguration` | LOW |
| `wifi_info` | via PowerShell `netsh wlan` | LOW |
| `lldp_cdp` | `DiscoveryProtocol` | LOW |
| `restart_adapter` | PowerShell `Restart-NetAdapter` | MEDIUM |
| `clear_dns_cache` | PowerShell `Clear-DnsClientCache` | MEDIUM |
| `renew_dhcp` | PowerShell `Set-NetIPInterface` | MEDIUM |
| `change_vlan` | device CLI (future) | HIGH |
| `modify_firewall_rule` | PowerShell `New-NetFirewallRule` | HIGH |
| `modify_device_config` | SSH/SNMP (future) | HIGH |
| `factory_reset` | device CLI (future) | CRITICAL — blocked by default |

Tool contract (rule #7):

```csharp
public sealed record AIToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required Type InputType { get; init; }     // strongly typed
    public required Type OutputType { get; init; }    // strongly typed
    public required RiskLevel RiskLevel { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required Func<object, CancellationToken, Task<object>> Executor { get; init; }
}
```

Requirements on every tool: clear name, clear description, typed input, typed output, permission level, timeout, error handling, logging.

---

## 8. Command execution security (rule #8)

No tool ever hands the AI a raw shell. Commands flow through:

```
Command Request → Command Validation → Policy Engine
  → Risk Classification → Permission → Approval → Execution → Result
```

- **Validation**: whitelist of permitted verbs/targets; reject anything that looks like arbitrary script injection.
- **No bypass**: the AI cannot call the execution layer except through a registered tool.
- **PowerShell** execution goes through `Microsoft.PowerShell.SDK` with constrained input; free-form script execution is not an AI-exposed tool.

---

## 9. Risk classification (normative)

| Level | Examples | Default behavior |
|---|---|---|
| LOW | ping, nslookup, traceroute, `Get-NetAdapter`, `Get-NetRoute`, `Get-NetIPConfiguration` | Execute per policy |
| MEDIUM | restart adapter, clear DNS cache, renew DHCP lease | Approval normally required |
| HIGH | change VLAN, modify firewall rule, change routing, modify device config, restart production equipment | Explicit approval required |
| CRITICAL | factory reset, delete config, disable firewall, mass config changes | Blocked by default |

**Invariant**: never silently downgrade a risk level.

---

## 10. Human-in-the-loop approval (rule #10)

Risky actions render an approval card with:

- Action, Target, Reason
- Expected effect, Potential impact
- Risk level
- Proposed command / configuration
- **Rollback method**

```text
ACTION REQUEST
Target:   SW-01
Action:   Change VLAN on Gi1/0/24
Reason:   Client requires VLAN 30
Risk:     HIGH
Impact:   Client connectivity may be interrupted.
Rollback: Restore VLAN 20
[Approve] [Reject]
```

High-risk network modifications are never applied without the required approval.

---

## 11. Secrets & credentials (rule #11)

- Never: hard-coded API keys/passwords, committed credentials, credentials in logs, secrets in error messages, or needless secret exposure to the AI.
- Config uses placeholders (e.g. `OPENAI_API_KEY=<secure-secret>`).
- Credential storage uses the platform's secure store (DPAPI-backed records in the existing profile/credential system, which already supports AES-encrypted profile files).
- Provider keys are read from secure config at request time, not stored in prompt or tool payloads.

---

## 12. Audit (rule #12)

Audit record fields: `Timestamp`, `User`, `Agent`, `Action`, `Target`, `Command`, `RiskLevel`, `Approval`, `Result`, `Error`. Secrets are never written to the audit log. Read-only diagnostics of LOW risk are still recorded (cheap, append-only).

---

## 13. AI response contract (rules #6, #17)

Structured output when appropriate:

```text
Problem / Evidence / Analysis / Likely Causes / Recommended Actions / Risk / Next Diagnostic Step
```

Epistemic labels rendered consistently:

- `[observed]` — directly read from a tool result.
- `[calculated]` — derived by deterministic computation.
- `[inferred]` — likely but not certain.
- `[assumed]` — precondition not verified.

A firewall "is blocking" is never stated as fact; the correct form is: *"The connection test failed on TCP/443. A firewall rule is one possible cause."*

---

## 14. Knowledge base / RAG (future, rule #26)

Sources: network documentation, SOPs, internal docs, vendor docs, troubleshooting guides, configuration standards. The AI must distinguish **Knowledge Base Evidence** (actually retrieved) from **Live Network Evidence** (measured) from **AI Inference**. A document is never cited unless its relevant content was actually retrieved.

---

## 15. Agent architecture (future, rule #25) — do not build prematurely

Candidate agents: Network Discovery, Troubleshooting, Monitoring, Security, Configuration, Documentation. The foundation (tool registry + policy + audit + provider abstraction) is built first, and a **single** copilot is shipped end-to-end before any multi-agent work is considered. No multi-agent implementation in Step 1.

---

## 16. Build, test & CI strategy

**The host used for authoring is Linux and cannot build or run WPF (.NET 10 Windows).** Build and test verification therefore runs on:

- **GitHub Actions `windows-latest`** — restore + build the full solution (x64, Release) on every push/PR; this is the merge gate.
- **The operator's real Windows machine** — interactive UI verification (WPF rendering, approval flow, real network targets), which CI cannot exercise.

Upstream ships **no automated tests**. TheWiseNetwork introduces its own test projects for **new** modules (AI tool registry, policy engine, audit, command validation, provider adapters) — pure logic that runs on both Windows CI and cross-platform. WPF view code stays manual-only.

Rule #19 (never claim a test passed unless it actually ran) applies verbatim: CI results are read back from GitHub Actions before they are reported.

---

## 17. Roadmap

| Step | Scope | Gate |
|---|---|---|
| **1** (this) | Repository fork, remotes, TRD, Windows CI, upstream preserved | CI builds clean on Windows; upstream unmodified |
| 2 | `NETworkManager.Audit` + `NETworkManager.Policy` (risk table + permission) + `NETworkManager.AI.Tools` typed registry foundation, with xUnit tests | Tests pass on CI |
| 3 | `NETworkManager.AI` provider abstraction + one local/OpenAI-compatible provider; copilot can run a single LOW tool (e.g. `ping`) through policy→audit | End-to-end slice verified |
| 4 | Human-in-the-loop approval UX + `NETworkManager.CommandExecution` (PowerShell, validated) | Approval flow verified on real Windows |
| 5 | Knowledge base (RAG) foundation | — |
| 6+ | Agents (only after the copilot foundation is stable) | — |

Each step has a defined scope; features outside the current step are recorded as proposals, not implemented (rule #28).

---

## 18. Decisions log

| ID | Decision |
|---|---|
| D-1 | Fork remains **GPL-3.0** (open source). |
| D-2 | Development repo: `taufiqnasrullah195/networkmanager-v2`, `upstream` remote = BornToBeRoot/NETworkManager. |
| D-3 | Build/test via GitHub Actions `windows-latest`; interactive UI verification on the operator's Windows machine. |
| D-4 | Extend, do not rewrite: TheWiseNetwork code lives in new sibling projects under `Source/`; `Source/3rdparty/` is off-limits. |

## 19. Known limitations / open questions

- This Linux host cannot run the Windows build; all build/test claims are sourced from GitHub Actions or the operator's Windows machine.
- The provider key management scheme (exact DPAPI/secret-store integration point) is specified in Step 3, not Step 1.
- Device configuration tooling (SSH/SNMP) is future; the command-execution layer in Step 3/4 is Windows/PowerShell-first.
# TheWiseNetwork — Network Tools & AI-Ready Tool Registry (Step 3)

Foundation for AI-assisted network diagnostics. The AI layer is **not** a shell: every future
AI action routes through a typed, validated, rate-classified tool. This step ships the
abstraction, the registry, the execution boundary, and an initial set of read-only diagnostic
tools — with **no** real AI provider wired in yet.

## Guiding principle

```
OBSERVE → STRUCTURE → VALIDATE → EXECUTE SAFELY → RETURN EVIDENCE
```

AI responses must always separate *observed facts* from *calculated values*, *inferences*, and
*recommendations*. A tool returns structured evidence; the (future) AI layer interprets it.

## Layered flow

```
Future AI Provider (IAIProvider)
        ↓ tool-call
AI Tool Registry (IToolRegistry)          "which tools exist?"
        ↓ resolve name
Tool Execution Service (IToolExecutionService)   "how is a tool executed?"
        ↓ validate → permission → execute (timeout + cancellation)
Network Tool (INetworkTool)               "one diagnostic action"
        ↓
NETworkManager engine / OS / network       "the real work"
```

The AI layer never calls an arbitrary OS command. It can only name a tool and pass typed input.

The `internet_connectivity_diagnostic` tool (Step 6) is a **high-level, composite, read-only** capability: it runs
the Internet Connectivity Diagnostic workflow through the `DiagnosticEngine` and returns a `DiagnosticReport`. A
future Custom Agent requests it as a single tool call and reasons over the resulting evidence — it never needs to
know the internal steps. See `docs/DIAGNOSTIC_ENGINE.md`.

## Project layout

| Project | Target framework | Purpose |
| --- | --- | --- |
| `NETworkManager.AI` | `net10.0` (cross-platform) | Abstractions (`IAIProvider`, `INetworkTool`, `IToolRegistry`, `IToolExecutionService`) + models (inputs, outputs, results, risk). No Windows dependency, no provider SDK. |
| `NETworkManager.AI.Tools` | `net10.0-windows10.0.22621.0` | Concrete tools wrapping the NETworkManager engine (and Win32 for routing). |
| `NETworkManager.AI.Tests` | `net10.0` (cross-platform) | xUnit tests for the registry, execution service, and input validation. Runs on Windows CI **and** on a Linux host. |

Splitting `AI` (portable) from `AI.Tools` (Windows) is deliberate: the tool-contract core can be
tested anywhere, while tools that need a live network stack stay Windows-only.

## Tool catalog (initial set)

| Tool | Category | Risk | Approval | Input → Output | Backing engine |
| --- | --- | --- | --- | --- | --- |
| `ping` | Connectivity | LOW | No | `PingInput` → `PingResult` | `NETworkManager.Models.Network.Ping` |
| `dns_lookup` | Resolution | LOW | No | `DnsLookupInput` → `DnsLookupResult` | `NETworkManager.Models.Network.DNSLookup` |
| `tcp_test` | Connectivity | LOW | No | `TcpTestInput` → `TcpTestResult` | `System.Net.Sockets.TcpClient` |
| `traceroute` | PathDiscovery | LOW | No | `TracerouteInput` → `TracerouteResult` | `NETworkManager.Models.Network.Traceroute` |
| `network_adapter_info` | NetworkInterface | LOW | No | `NetworkAdapterInput` → `NetworkAdapterResult` | `NETworkManager.Models.Network.NetworkInterface` |
| `routing_table` | Routing | LOW | No | `RoutingTableInput` → `RoutingTableResult` | Win32 `GetIpForwardTable` (read-only) |
| `internet_connectivity_diagnostic` | Connectivity | LOW | No | `InternetConnectivityDiagnosticInput` → `DiagnosticReport` | `DiagnosticEngine` (Step 6) |
| `network_monitoring_status` | Monitoring | LOW | No | `MonitoringStatusInput` → `MonitoringStatusResult` | `IMonitoringQuery` / `MonitoringEngine` (Step 9, read-only) |

All six core tools are **LOW risk and read-only** by design: diagnostics first, no mutation, no shell
execution. `routing_table` currently supports IPv4 only; IPv6 returns a structured
`NotImplemented` error (see Known Issues).

## Risk model

`ToolRiskLevel` ∈ { Low, Medium, High, Critical }. Default behaviors (enforced by the execution
service's approval gate, expanded in later steps):

- **LOW** — execute per policy (no approval).
- **MEDIUM** — approval normally required.
- **HIGH** — explicit approval required.
- **CRITICAL** — blocked by default.

The risk level is declared per tool and is **never silently downgraded**.

## Execution pipeline

`ToolExecutionService.ExecuteAsync` performs, in order:

1. **Resolve** — find the tool by name (case-insensitive); unknown name → `ToolNotFound`.
2. **Type check** — input must be an instance of the tool's `InputType` → `InvalidInputType`.
3. **Approval gate** — if the tool `RequiresApproval` but no approval was granted → `ApprovalRequired`.
4. **Validate** — inputs implementing `IValidatableToolInput` report violations → `ValidationFailed`.
5. **Execute** — the tool runs under a timeout and the caller's cancellation token.
6. **Enrich** — the raw `ToolOutcome` is wrapped in a `ToolResult` carrying name, risk, timestamp, and duration.

Timeout and cancellation are enforced by the **service**, not trusted to individual tools:
engines that ignore cancellation (e.g. `DNSLookup`) are still bounded by the service. Each tool
also exposes its own `Timeout` (e.g. traceroute gets 180 s; a single DNS query gets 30 s).

## Result contract

```csharp
public sealed record ToolResult
{
    string ToolName;          // which tool ran
    ToolRiskLevel RiskLevel;  // declared risk
    bool Success;
    bool RequiresApproval;
    DateTimeOffset Timestamp;
    TimeSpan Duration;
    object? Data;             // typed output (e.g. PingResult)
    string? Error;
    string? ErrorCode;        // machine-readable: ToolNotFound, InvalidInputType,
                              //   ValidationFailed, ApprovalRequired, TimedOut,
                              //   Cancelled, ExecutionError, plus tool-specific codes
}
```

Error codes are stable identifiers so a future AI/audit layer can branch on them, not on prose.

## Adding a new tool

1. Define typed input (and `Validate()`) and output records in `NETworkManager.AI.Models`.
2. Implement `INetworkTool` in `NETworkManager.AI.Tools` (wrap an existing engine — do not
   reimplement network logic).
3. Register it in `NetworkToolCollection.All()`.
4. Add tests to `NETworkManager.AI.Tests`.
5. Update this document's catalog table.

## Deliberately out of scope (Step 3)

No LLM/provider integration, no chat UI, no MCP, no RAG, no database, no autonomous agents, no
automatic remediation, no firewall/VLAN/config modification, no unrestricted PowerShell. The
`IAIProvider` interface exists only as a seam; no provider SDK is referenced.

## Security notes

- No secrets or API keys; `IAIProvider` carries no credentials.
- No shell execution anywhere in the tool path.
- `routing_table` uses read-only Win32 APIs, not `route print` or PowerShell.
- Diagnostic output is structured and redaction-friendly (no raw credentials are ever surfaced).
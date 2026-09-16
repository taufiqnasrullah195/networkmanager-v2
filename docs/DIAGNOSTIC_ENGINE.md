# TheWiseNetwork — Diagnostic Engine (Step 6)

STEP 6 combines the individual network tools into **read-only, structured diagnostic workflows**. The engine answers
*"what should we check?"* and collects evidence; it does **not** answer *"why is this happening?"* — that belongs to the
future AI layer, which will reason over the evidence this engine produces.

## Guiding principle

```
OBSERVE → COLLECT → STRUCTURE → CLASSIFY → EXPLAIN
```

The engine collects evidence first. It never guesses: it reports symptoms and lets a deterministic analyzer classify
them, using "failed"/"unreachable"/"detected" language rather than unproven root-cause claims.

## Architecture

```
AI Provider / Custom Agent
  |
  v
Tool Orchestrator (STEP 5)
  |
  v
Diagnostic Engine (IDiagnosticEngine)          <- deterministic, no AI, no network logic
  |
  v
Tool Registry → Tool Execution Service (STEP 3)
  |
  v
Network Tools (ping, dns_lookup, tcp_test, …)
```

The diagnostic engine **reuses** the tool registry and execution service — it never calls an AI provider and never
performs networking itself. It coordinates typed tools and interprets their structured output.

## Components

| Type | Responsibility |
| --- | --- |
| `DiagnosticWorkflow` | A named, ordered sequence of `DiagnosticStep`s with severity + timeout. |
| `DiagnosticStep` | One check: which tool, what arguments, dependencies, timeout, required?, evaluator, classification. |
| `DiagnosticTarget` | Optional target fields (hostname, IP, port, interface, network). |
| `DiagnosticEvidence` | One structured observation (step, tool, target, success, data, error, timestamp, duration). |
| `DiagnosticStepResult` | A step's outcome: `Passed / Failed / Warning / Skipped / Cancelled`. |
| `DiagnosticReport` | The run result: status, ordered steps, derived evidence/failed-checks, summary. |
| `IDiagnosticAnalyzer` | Deterministic rules → `DiagnosticAnalysis` (classification + findings + recommendation). |
| `InternetConnectivityDiagnosticTool` | Exposes the workflow as one registered `INetworkTool`. |

## The Internet Connectivity Diagnostic

The implemented workflow (`InternetConnectivityDiagnostic.Create()`) runs eight read-only checks, in order:

| # | Step id | Tool | What it checks | Classifies as |
| --- | --- | --- | --- | --- |
| 1 | `adapter` | `network_adapter_info` | ≥1 adapter is `Up` | `NoNetworkAdapter` |
| 2 | `ip_config` | `network_adapter_info` | an active adapter has an IPv4 address | `NoIpAddress` |
| 3 | `default_route` | `routing_table` | a `0.0.0.0/0` route exists | `NoDefaultGateway` |
| 4 | `gateway_ping` | `ping` | default gateway responds (gateway taken from prior evidence) | `GatewayUnreachable` |
| 5 | `dns_lookup` | `dns_lookup` | a host name resolves | `DnsFailure` |
| 6 | `external_ping` | `ping` | an external IP responds | `ExternalConnectivityFailure` |
| 7 | `tcp_test` | `tcp_test` | a TCP connect to host:port works | `TcpConnectivityFailure` |
| 8 | `traceroute` | `traceroute` | (non-required) path to target | `ExternalConnectivityFailure` |

Steps 4–8 derive their arguments from the target and from already-collected evidence (e.g. the gateway IP is read
from the `ip_config` step's `NetworkAdapterResult`, with a fallback to the default route's next hop). The workflow is
defined as data + small delegates — not one giant class.

## Dependencies: skipped vs failed

Each step declares `DependsOn`. A step runs only if **every** dependency **PASSED**; otherwise it is **SKIPPED** with a
reason, not failed:

- No active adapter → `ip_config` … `traceroute` are all SKIPPED.
- No IPv4 address → `default_route` … `traceroute` are SKIPPED.
- Gateway unreachable → `dns_lookup`, `external_ping`, `tcp_test`, `traceroute` are SKIPPED.

This avoids misleading results: a skipped check is *intentionally not run*, never reported as a failure. Only a
required step that actually executed and failed marks the diagnostic `FAILED`.

## Evidence model

Evidence is the observed truth, preserved in order:

```json
{
  "stepId": "gateway_ping",
  "tool": "ping",
  "target": "192.168.1.1",
  "success": false,
  "data": { "sent": 4, "received": 0, "packetLossPercent": 100 },
  "error": "Target did not respond to ping.",
  "errorCode": "CheckFailed",
  "timestamp": "…",
  "duration": "…"
}
```

Underlying typed tool outputs (`PingResult`, `TcpTestResult`, `DnsLookupResult`, `RoutingTableResult`, …) are
preserved as the `data` payload — never flattened into prose — so the future AI can interpret them.

## Failure classification

`FailureClass` (and the deterministic analyzer) map the **first failed required step** to a classification. The mapping
is declared per step (`DiagnosticStep.ClassifiesAs`), so the analyzer stays workflow-agnostic:

| Failure path | Classification |
| --- | --- |
| no active adapter | `NoNetworkAdapter` |
| no IPv4 on active adapter | `NoIpAddress` |
| no default route | `NoDefaultGateway` |
| gateway does not respond | `GatewayUnreachable` |
| DNS lookup fails | `DnsFailure` |
| external IP not reachable | `ExternalConnectivityFailure` |
| TCP connect fails | `TcpConnectivityFailure` |
| a step times out | `Timeout` |

The analyzer **reports a symptom, not a root cause** — e.g. *"DNS resolution failed."* and *"Check the configured DNS
servers."*, never *"the DNS server is broken"*.

## Cancellation & timeout

- The workflow has an overall `Timeout` (enforced via a linked cancellation source) and each step has its own `Timeout`
  (enforced via `WaitAsync`); individual tools are bounded by their own timeout through the execution service.
- On cancellation: the current step stops where practical and remaining steps are marked `Cancelled`; final status is
  `CANCELLED`.

## Security

The engine is **read-only by construction**. It only ever dispatches registered tools through the execution service — it
cannot change IP/DNS/routes/adapters, renew DHCP, modify the firewall/VLAN, or execute PowerShell/shell commands. A
single tool failure does not crash the engine: it becomes a structured `FAILED` step.

## Custom Agent integration

A future Custom Agent requests the diagnostic as one high-level tool:

```json
{ "toolName": "internet_connectivity_diagnostic", "arguments": { "hostname": "example.com", "port": 443 } }
```

The agent receives a structured `DiagnosticReport` and reasons over the evidence — it never needs to know the internal
eight steps. The diagnostic tool is `LOW` risk and requires no approval.

## Resource limits

No infinite loops (workflow steps are a fixed, validated list), no unbounded traceroute (bounded by the `traceroute`
tool's `MaximumHops`), bounded timeouts at every level, and sequential execution (no uncontrolled parallelism).
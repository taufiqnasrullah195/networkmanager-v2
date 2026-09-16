# TheWiseNetwork — AI Tool Orchestration

STEP 5 connects the provider layer (STEP 4) to the tool layer (STEP 3) through a **controlled orchestration
boundary**. The AI provider — or an external Custom Agent — may *request* registered tools, but it never touches
the network, a shell, or the filesystem directly. Every operation flows through the orchestrator, which enforces
validation, policy, approval, and execution discipline.

## 1. Architecture

```
User
  |
  v
AI Provider / Custom Agent        (IAIProvider / CustomAgentProvider)
  |
  v
AIToolCall                        (provider-neutral tool request)
  |
  v
Tool Call Orchestrator            (IToolCallOrchestrator)
  |
  +--> Tool Registry              (IToolRegistry — allowlist)
  |
  +--> Policy                     (IToolPolicyService)
  |
  +--> Approval                   (IToolApprovalService)
  |
  v
Tool Execution Service            (IToolExecutionService)
  |
  v
Network Tool                       (INetworkTool — e.g. ping, dns_lookup)
  |
  v
Structured Tool Result             (ToolResult / AIToolResult)
  |
  v
AI Provider / Custom Agent         (feedback for the next reasoning turn)
```

Responsibilities are deliberately split:

| Layer | Responsibility | Never does |
| --- | --- | --- |
| AI Provider | Reason, decide which tool to ask for, interpret evidence. | Execute tools, run commands. |
| Orchestrator | Receive, resolve, validate, authorise, dispatch, capture. | Perform network operations. |
| Tool Registry | Own the allowlist of typed tools. | Execute anything. |
| Policy / Approval | Decide allow / deny / require-approval. | Execute anything. |
| Tool Execution Service | Deserialize/validate input, enforce timeout/cancellation, invoke the tool. | Shell, arbitrary executables. |
| Network Tool | One typed network operation returning evidence. | Nothing outside its defined input. |

## 2. Tool call lifecycle

A single `AIToolCall` (`CallId`, `ToolName`, `ArgumentsJson`) traverses these steps in
`ToolCallOrchestrator.ExecuteAsync`:

1. **Cancellation gate** — a cancelled token returns a `Cancelled` outcome before anything is dispatched.
2. **Structure validation** — empty `CallId` or `ToolName` → `InvalidCall`.
3. **Resolution** — `IToolRegistry.TryGet(name)`. Unknown name → `ToolNotFound`. Tools are **never** created
   dynamically from an AI request.
4. **Policy** — `IToolPolicyService.Evaluate(tool, context)` → `Allow` / `Deny` / `RequireApproval`.
   `Deny` → `PolicyDenied` (not executed).
5. **Approval** — `IToolApprovalService.Decide(...)`. When policy is `RequireApproval` and approval is not
   granted → `ApprovalRequired` (not executed).
6. **Dispatch** — `IToolExecutionService.ExecuteAsync(call, context, ct)`, which deserializes the JSON arguments
   into the tool's typed input, validates ranges/types/required fields, and enforces timeout and cancellation.
7. **Capture** — the result is wrapped into a `ToolCallOutcome` carrying `CallId`, `ToolName`, the policy/approval
   decision, the structured `ToolResult` (with timestamp + duration), and any error code.

## 3. Validation

All argument handling is inherited from the execution service (STEP 3/4):

- **Valid JSON** — `System.Text.Json` (web defaults). Malformed JSON → `InvalidArguments`.
- **Field types** — a number where a string is expected → `InvalidArguments`.
- **Required fields & ranges** — enforced by `IValidatableToolInput.Validate()`; violations →
  `ValidationFailed`, e.g. *"Port must be between 1 and 65535."*

Arbitrary command strings are never accepted as tool arguments: the only input shape a tool accepts is its typed
`InputType`, and the registry is the only source of tools.

## 4. Policy

`IToolPolicyService` is a provider-neutral boundary: inputs are tool metadata + execution context, output is a
`PolicyDecision`. No policy logic lives inside individual tools.

The safe default (`DefaultToolPolicyService`):

| Risk | Decision |
| --- | --- |
| LOW (read-only diagnostics) | `Allow` |
| MEDIUM | `RequireApproval` |
| HIGH | `RequireApproval` |
| CRITICAL | `Deny` |

Future RBAC / enterprise policies extend this by supplying their own `IToolPolicyService`; nothing hard-codes
policy in the tools.

## 5. Approval

`IToolApprovalService` resolves approval given the policy decision and execution context
(`ApprovalResult`: `NotRequired` / `Approved` / `Rejected` / `Required`). The default reads
`ToolExecutionContext.ApprovalGranted`. The interactive approval UI is deliberately out of scope for STEP 5 — the
abstraction exists so a later step can present a real approval flow and set `ApprovalGranted` on the context.

## 6. Execution

The orchestrator calls `IToolExecutionService` and never touches `cmd.exe`, PowerShell, `bash`, arbitrary
executables, or the filesystem. The execution path is fixed:

```
Orchestrator → Tool Registry → Tool → ToolExecutionService → existing NETworkManager functionality
```

## 7. Result handling

Structured results are preserved end to end. The orchestrator returns a `ToolCallOutcome` (audit view: decision,
timing, `ToolResult`); `.ToAIToolResult()` projects it to a provider-facing `AIToolResult` that keeps `CallId`,
`ToolName`, `Success`, structured `Data`, and `ErrorCode`. Results are never collapsed into plain text — evidence
(target, timestamp, tool, result, error, duration) survives so the AI may interpret it later.

Stable error codes (PascalCase, step-consistent): `InvalidCall`, `ToolNotFound`, `PolicyDenied`,
`ApprovalRequired`, `InvalidArguments`, `ValidationFailed`, `InvalidInputType`, `TimedOut`, `Cancelled`,
`ExecutionError`, `ToolCallLimitReached`.

## 8. Multiple tool calls

`ToolCallOrchestrator.ExecuteAsync(IReadOnlyList<AIToolCall>, ...)` executes calls **sequentially** (avoiding
uncontrolled parallelism), preserving `CallId` on each outcome and stopping on cancellation.

## 9. The agent tool loop

`AgentToolLoop` (`IAgentToolLoop`) runs the full reasoning loop:

```
prompt provider → execute its tool calls → feed structured results back → repeat → final response
```

It advertises tool definitions from the registry (`Tools` on each request) and feeds back the accumulated
`ToolResults`. The loop is **never unbounded** — see loop protection below.

## 10. Loop protection

`AgentToolLoop` enforces three bounds (`AgentLoopOptions`, configurable):

- **`MaxToolRounds`** (default 5) — hard cap on provider round-trips that request tools.
- **`MaxToolExecutions`** (default 20) — hard cap on total tool executions across the loop.
- **`MaxRepeatedCalls`** (default 3) — an identical `toolName + arguments` pair may not repeat beyond this count.

When any bound is hit, the loop returns `LimitReached = true` with `ErrorCode = ToolCallLimitReached` and does **not**
execute further tools.

## 11. Cancellation & timeout

- A caller-cancelled token stops the orchestrator from dispatching (returns `Cancelled`), stops future tools in a
  batch, and — combined with a provider that observes the token — terminates the loop.
- `ToolExecutionService` enforces each tool's `Timeout` via `WaitAsync`; a tool that overruns returns `TimedOut`
  rather than hanging.

The orchestrator fails safe: no AI request can hang indefinitely.

## 12. Security model

The following are **impossible** for a Custom Agent / AI provider because there is no code path for them —
execution only ever reaches a registered `INetworkTool` through the orchestrator's fixed pipeline:

- arbitrary command execution (`cmd.exe`, PowerShell, `bash`),
- arbitrary executables,
- direct filesystem modification,
- direct network configuration.

Only tools present in the registry (the **allowlist**) can run. An AI sending `run_nmap` (or any unregistered name)
receives `ToolNotFound` and nothing executes. Arguments are confined to the tool's typed input model.

## 13. Logging & audit preparation

`IToolOrchestrationLogger` emits structured, secret-free events
(`ToolCallReceived`, `ToolResolved`, `ToolValidationFailed`, `PolicyDenied`, `ApprovalRequired`,
`ToolExecutionStarted`, `ToolExecutionCompleted`, `ToolExecutionFailed`, `ToolCallLimitReached`). Tool argument
values and error bodies are **never** logged. `ToolCallOutcome` and `ToolResult` already carry the fields a future
persistent audit store needs (timestamp, user, provider, agent, tool, target, risk, decision, result, duration).
# TheWiseNetwork — AI Providers (Step 4)

The AI layer talks to AI backends exclusively through `IAIProvider`. The application never
references a vendor SDK — the provider-neutral request/response models sit in `NETworkManager.AI`,
and each provider adapts them to its own transport/protocol.

## Architecture

```
                    TheWiseNetwork
                           |
                    AI Provider Layer (IAIProvider)
                           |
             +-------------+-------------+
             |             |             |
        Future API      Local AI     Custom Agent
        Providers    Provider        Provider (implemented)
        (NOT IMPLEMENTED) (NOT IMPLEMENTED) |
                                        |
                                        v
                                  External Agent
                                        |
                                        v
                                  Tool Request
                                        |
                                        v
                                  Tool Registry → Policy/Validation → Tool Execution
                                        |
                                        v
                                  Network Services
```

The **provider layer** translates and transports. **TheWiseNetwork** remains responsible for tool
availability, validation, permission, risk, execution, evidence, and security. A provider never
executes a tool and never holds credentials.

## Core abstractions (`NETworkManager.AI.Abstractions`)

### `IAIProvider`

```csharp
public interface IAIProvider
{
    AIProviderCapability Capabilities { get; }                       // chat | tool-calling | ...
    Task<AIResponse> SendAsync(AIRequest request, CancellationToken ct = default);
}
```

Minimal by design — no provider-specific concepts leak into the interface.

### `IAIProviderRegistry`

Registers and selects providers by name; does not send requests.

```csharp
public interface IAIProviderRegistry
{
    IReadOnlyList<string> Names { get; }
    void Register(string name, IAIProvider provider);
    bool TryGet(string name, out IAIProvider? provider);
    void Select(string? name);
    IAIProvider Resolve(string? name = null);
}
```

### `IAgentAuthentication`

Applies credentials to an outgoing request. The secret is supplied by the implementing class at
runtime (from secure configuration), never hard-coded. Step 4 ships two implementations:

- `ApiKeyAuthentication` — adds a configurable header (default `X-Api-Key`).
- `BearerTokenAuthentication` — adds `Authorization: Bearer <token>`.

Both accept the secret via a `Func<string>` delegate so a future credential store can plug in.

## Request / response models (`NETworkManager.AI.Models`)

### `AIRequest` — provider-neutral request

| Field | Type | Notes |
| --- | --- | --- |
| `SystemPrompt` | `string?` | |
| `UserPrompt` | `string?` | |
| `Conversation` | `IReadOnlyList<ChatMessage>?` | role/content history |
| `Tools` | `IReadOnlyList<AIToolDefinition>?` | advertised tools (from the registry) |
| `ToolResults` | `IReadOnlyList<AIToolResult>?` | structured evidence from prior turns |
| `Context` | `IReadOnlyDictionary<string,string>?` | controlled context (hostname, device…); never secrets |
| `Metadata` | `IReadOnlyDictionary<string,string>?` | |
| `ConversationId` | `string?` | optional identity; the agent decides how to manage state |

### `AIResponse` — provider-neutral response

| Field | Type | Notes |
| --- | --- | --- |
| `Success` | `bool` | |
| `Text` | `string?` | |
| `ToolCalls` | `IReadOnlyList<AIToolCall>?` | tool calls requested by the provider |
| `FinishReason` | `AIFinishReason` | stop / length / tool_calls / content_filtered / unknown |
| `Error` / `ErrorCode` | `string?` / `ProviderErrorCode?` | agent-level failure signalling |
| `Usage` | `AIUsage?` | optional token counts |
| `Metadata` | `IReadOnlyDictionary<string,string>?` | |

### `AIToolCall` — tool-call representation

```csharp
public sealed record AIToolCall
{
    string CallId;          // stable id
    string ToolName;        // e.g. "ping"
    string ArgumentsJson;   // JSON string, e.g. {"target":"192.168.1.1"}
}
```

The provider layer only *represents* tool calls and returns them to the caller. Execution happens
later, in `ToolExecutionService.ExecuteAsync(AIToolCall, …)`, which deserializes arguments into the
tool's typed input and runs the registry/validation/risk pipeline. Unknown tools are rejected
(`ToolNotFound`); tools are never created from a provider request.

### `AIToolResult` — structured evidence

Returned to a provider/agent after a tool runs, preserving structured data (no free-text munging).

### `AIToolDefinition` — a tool advertised to the provider

Built by `ToolDefinitionBuilder` from an `INetworkTool` (name, description, risk level, and a simple
reflected name→type input schema). Only tools registered in the tool registry may be advertised.

## Capabilities

`AIProviderCapability` (flags): `Chat`, `ToolCalling`, `Streaming`, `Vision`, `StructuredOutput`,
`Embeddings`. Providers may implement a subset; `CustomAgentProvider` and `MockAIProvider` currently
report `Chat | ToolCalling`. Streaming is documented as a future capability and is **not** implemented.

## Error model

`ProviderException` with a stable `ProviderErrorCode`:

`ProviderUnavailable`, `AuthenticationFailed`, `Timeout`, `InvalidRequest`, `InvalidResponse`,
`ToolCallInvalid`, `RateLimited`, `NetworkError`, `Unknown`.

Transport/authentication/parse/timeout failures surface as typed `ProviderException`s; internal
stack traces are never shown to users.

## CustomAgentProvider

`CustomAgentProvider` talks to an external/custom agent over the documented HTTP protocol
(`docs/CUSTOM_AGENT_PROTOCOL.md`). Behavior:

- **HTTPS** is the normal transport; HTTP is allowed for local development. TLS certificate
  validation is never disabled (no "accept-all-certificates" path).
- **Timeout** is configurable per endpoint and enforced via a linked cancellation token — a slow or
  dead agent fails with `ProviderException(Timeout)`, never a frozen UI.
- **Cancellation** propagates the caller's `CancellationToken`.
- **Retry** is bounded (default 0) and limited to transient failures (`ProviderUnavailable`,
  `NetworkError`, `Timeout`, `RateLimited`). Authentication failures and malformed responses are
  never retried.
- **Credentials** come from `IAgentAuthentication`, never from configuration.

The custom agent can *request* tools; it cannot execute them. `execute_powershell`, `cmd.exe`,
filesystem writes, and raw network changes are impossible through this path — the only operational
path is a registered tool → validation → policy → execution.

## MockAIProvider

`MockAIProvider` is a test/double provider driven by a `Func<AIRequest, CancellationToken,
Task<AIResponse>>` handler, letting tests (and development builds) simulate success, tool calls,
timeouts, and errors with no external service.

## Security model

1. No vendor SDKs, no credentials in the provider layer.
2. No shell / arbitrary executable path from any provider.
3. Tool calls are validated against the registry; unknown tools are rejected.
4. Context passed to a provider is explicit and controlled — never passwords, keys, or secrets.
5. TLS validation is not weakened; production configurations prefer HTTPS.
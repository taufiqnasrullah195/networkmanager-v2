# TheWiseNetwork — Custom Agent Protocol

> **This protocol is an internal TheWiseNetwork provider protocol. It does not claim compatibility
> with OpenAI-compatible APIs, Anthropic APIs, MCP, or any other standard.**

A *custom agent* is an external reasoning/orchestration endpoint TheWiseNetwork can talk to. The
agent may run locally, on another machine, in a container, or in an enterprise environment. The
agent can **request** tools; it can never execute them — tool execution stays inside TheWiseNetwork.

Protocol version: **1**.

## Transport

- HTTP/HTTPS, JSON body, `Content-Type: application/json`.
- `Endpoint` is the agent API base URL; TheWiseNetwork POSTs to `{Endpoint}/chat`.
- HTTPS is preferred for any non-local deployment. TLS certificate validation is never disabled.

## Endpoint

```
POST {Endpoint}/chat
```

Example: with `Endpoint = https://agent.example.internal/api/v1`, the request goes to
`https://agent.example.internal/api/v1/chat`.

## Request schema

```json
{
  "protocolVersion": "1",
  "conversationId": "optional-id",
  "systemPrompt": "You are a network engineer.",
  "message": "Why can't I reach 10.10.20.50?",
  "conversation": [
    { "role": "user", "content": "…" },
    { "role": "assistant", "content": "…" }
  ],
  "tools": [
    {
      "name": "ping",
      "description": "Test ICMP connectivity to a host",
      "riskLevel": "Low",
      "inputSchema": { "target": "string", "count": "integer", "timeoutMilliseconds": "integer" }
    }
  ],
  "toolResults": [
    { "toolName": "ping", "success": true, "data": { "target": "192.168.1.1", "received": 4 } }
  ],
  "context": { "hostname": "SRV-01" },
  "metadata": {}
}
```

All fields except `protocolVersion` are optional. `tools` are built by TheWiseNetwork from the tool
registry only. `toolResults` carry structured evidence (never ambiguous free text).

## Response schema

```json
{
  "success": true,
  "message": "I need to test connectivity first.",
  "finishReason": "tool_calls",
  "toolCalls": [
    { "id": "call-001", "toolName": "ping", "arguments": { "target": "10.10.20.50" } }
  ],
  "usage": { "inputTokens": 12, "outputTokens": 8 },
  "error": null,
  "errorCode": null,
  "metadata": {}
}
```

`finishReason` ∈ `stop | length | tool_calls | content_filtered` (client tolerates unknown values).

When the agent itself fails, it may respond `"success": false` with `error`/`errorCode`.

## Tool call schema

```json
{ "id": "call-001", "toolName": "ping", "arguments": { "target": "10.10.20.50" } }
```

`arguments` is a JSON object. On receipt TheWiseNetwork:

1. Confirms `toolName` exists in the tool registry (otherwise rejects with `TOOL_NOT_FOUND`).
2. Deserializes `arguments` into the tool's typed input.
3. Validates input, checks risk/permission.
4. Executes through `ToolExecutionService`.
5. Returns a structured tool result.

A request for an unregistered tool (e.g. `execute_powershell`) is rejected; tools are never created
dynamically from an agent request.

## Tool result schema

```json
{ "toolName": "ping", "success": true, "data": { "target": "192.168.1.1", "received": 4 }, "error": null, "errorCode": null }
```

## Error handling

Transport-level failures surface inside TheWiseNetwork as typed `ProviderException`s (not as HTTP
responses of this protocol):

| Condition | Provider `ProviderErrorCode` |
| --- | --- |
| HTTP 401/403 | `AuthenticationFailed` |
| HTTP 429 | `RateLimited` |
| Other non-200 | `ProviderUnavailable` |
| Timeout | `Timeout` |
| Connection failure | `NetworkError` |
| Unparseable/empty 200 body | `InvalidResponse` |

## Authentication concept

Authentication is handled by `IAgentAuthentication` (e.g. `ApiKeyAuthentication` /
`BearerTokenAuthentication`). The agent endpoint must be protected; the secret lives in
TheWiseNetwork's secure configuration and is attached as a header. It is never placed in logs or
errors.

## Example interaction (tool-call loop)

1. TheWiseNetwork → agent: `POST /chat` with user question + `tools: [ping]`.
2. Agent → TheWiseNetwork: response with `toolCalls: [{ "toolName": "ping", "arguments": { … } }]`.
3. TheWiseNetwork executes `ping` through `ToolExecutionService`.
4. TheWiseNetwork → agent: `POST /chat` with `toolResults: [ … structured ping evidence … ]`.
5. Agent → TheWiseNetwork: final answer (or further tool calls, bounded by the caller).

The agent reasons; TheWiseNetwork runs the tools, applies policy, and owns the evidence.
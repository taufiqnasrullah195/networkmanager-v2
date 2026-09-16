# TheWiseNetwork — AI Conversation (Step 7)

STEP 7 introduces the first real AI interaction layer. A user asks a network question; the conversation service
orchestrates the provider, routes tool calls through the orchestrator, triggers diagnostic workflows, and returns an
**evidence-backed** answer that keeps observed facts, inferences, and recommendations separate. In-memory only — no
database, no RAG, no long-term memory.

## Architecture

```
User
  |
  v
AI Conversation Service (IAIConversationService)
  |
  v
AI Provider (IAIProvider → CustomAgentProvider / MockAIProvider)
  |
  v
AIToolCall
  |
  v
Tool Orchestrator (STEP 5)
  |
  v
Diagnostic Engine (STEP 6)
  |
  v
Evidence
  |
  v
AI Provider (reasoning over evidence)
  |
  v
Structured Response (AIAnalysisResponse)
```

The conversation service never performs networking and never talks to a provider SDK directly — it communicates only
through `IAIProvider` (wrapped by the bounded `IAgentToolLoop`). The AI reasons; TheWiseNetwork controls execution.

## Conversation model

- `AIConversation` — in-memory conversation identity + message list (not persisted).
- `AIMessage` — provider-neutral message with `Role` (System/User/Assistant/Tool), optional `Content`, `ToolCalls`,
  and `ToolResults`, plus timestamp.
- `ConversationStatus` — Idle / Processing / WaitingForTool / WaitingForProvider / Completed / Failed / Cancelled.

## Conversation flow

`AIConversationService.SendAsync(userMessage, conversationId?, ct)`:

1. Resolve or create the in-memory conversation; append the user message.
2. Build a provider-neutral `AIRequest`: base system instructions + prior evidence summary (`SystemPrompt`),
   redacted `UserPrompt`, and prior conversation history (`Conversation`).
3. Run the **existing** bounded tool loop (`IAgentToolLoop`, STEP 5) — maximum rounds/repeated-call limits are reused,
   not duplicated.
4. Record tool activity and evidence provenance back into the conversation.
5. Build a structured `AIAnalysisResponse` and return it.

## Tool calling

Tool calls go **only** through the `ToolCallOrchestrator` — the conversation service never bypasses it, and the AI
never reaches `IToolExecutionService` directly:

```
AI → ToolCallOrchestrator → Diagnostic Engine → Tool Execution
```

## Diagnostic integration

An AI may request the registered `internet_connectivity_diagnostic` tool (STEP 6). The orchestrator runs the
`DiagnosticEngine`, which returns a structured `DiagnosticReport`. The conversation service then:

- runs the deterministic `IDiagnosticAnalyzer` to obtain a `FailureClass`,
- builds an `AIEvidenceContext` (diagnostic id, timestamp, target, evidence, failed checks, warnings, classification),
- unrolls each observed step into an `Observation` finding with an evidence reference,
- adds an `Inference` finding for the classification and a `Recommendation` finding from the analyzer,
- records the context so later turns can cite the provenance.

## Evidence handling & provenance

Every observation carries a reference (`{DiagnosticId}:{StepId}`) into the structured evidence, and every diagnostic
run is preserved as an `AIEvidenceContext`. Prior-turn evidence is re-injected into the system prompt as **facts only**
— never prose that could mislead.

Example provenance:

```json
{ "stepId": "dns_lookup", "tool": "dns_lookup", "target": "example.com", "success": false, "error": "DNS resolution failed." }
```

## Fact vs inference

`AIAnalysisResponse` is the provider-neutral structured answer:

- `Findings` — typed `AIFinding`s: `Observation` (measured fact, `Confirmed`), `Inference` (`Supported`),
  `Warning`, `Recommendation` (`Possible`).
- `Confidence` — qualitative (`Unknown`/`Possible`/`Supported`/`Confirmed`); no fabricated numeric score.
- `PossibleCauses` — deterministic hypotheses per failure class, each labelled `(unverified)`.
- `Recommendations` / `Summary` / `EvidenceReferences` / `EvidenceContexts` / `RawText`.

The deterministic analyzer (STEP 6) drives the inference; the provider only contributes natural-language framing. If
no diagnostic evidence exists, `Confidence` is `Unknown`, the findings list is empty, and **nothing is fabricated** —
"not verified / insufficient evidence" is the fallback, never an invented result.

## System instructions

`NetworkDiagnosticInstructions.BaseSystemPrompt` is the controlled, provider-neutral base instruction: use tools when
evidence is required, never fabricate results, distinguish facts from inferences, identify uncertainty, never claim an
unverified change, and — critically — the AI has **no unrestricted system access** and must never try to bypass tool
restrictions. No vendor-specific prompt syntax.

## Security & sensitive data

- `ISensitiveDataFilter` (`DefaultSensitiveDataFilter`) redacts bearer tokens and key-like assignments
  (`password=…`, `api_key: …`, `token=…`) before content is sent to the provider.
- The conversation service never invokes shells, never changes configuration, and only reaches tools through the
  orchestrator.

## Cancellation & error handling

Cancellation propagates to the loop, the orchestrator, and the diagnostic engine. Provider failures (unavailable /
timeout / invalid / empty responses) map to `Failed` with a user-friendly summary — never a stack trace. A pre-cancelled
token returns `Cancelled` without dispatching tools.

## Context limits

Conversation history is bounded (`maxContextMessages`, default 20): oldest messages are trimmed while recent tool
results and the current request are preserved; the system instruction lives in `SystemPrompt`, independent of history.

## UI

No WPF chat UI is added in STEP 7. The conversation layer is fully testable headlessly (the UI is Windows-only and
cannot be built on this host). A future step can bind a minimal chat surface to `IAIConversationService` and render
`AIAnalysisResponse` findings, tool activity, and evidence.
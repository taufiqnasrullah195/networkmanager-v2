# TheWiseNetwork — AI Copilot UI (Step 8)

STEP 8 binds the provider-neutral conversation layer (Step 7) to a **minimal WPF chat surface** inside the
NETworkManager shell. No UI redesign — one new application view, registered through the same application list,
navigation, and MahApps styling as every existing tool.

## Location

| File | Purpose |
| --- | --- |
| `Source/NETworkManager/ViewModels/AICopilotViewModel.cs` | MVVM view model (no code-behind logic) |
| `Source/NETworkManager/Views/AICopilotView.xaml` / `.cs` | The chat surface (UserControl) |
| `Source/NETworkManager/AICopilotFactory.cs` | Manual composition root (registry → execution → diagnostics → orchestrator → loop → conversation service) |

Registration touches (minimal, pattern-identical to existing apps):

- `NETworkManager.Models/ApplicationName.cs` — new `AICopilot` enum member.
- `NETworkManager.Models/ApplicationManager.cs` — icon case (`PackIconMaterialKind.RobotOutline`).
- `NETworkManager/MainWindow.xaml.cs` — one field + one `case ApplicationName.AICopilot:` in `OnApplicationViewVisible`.

The application appears in the standard application list (`ApplicationManager.GetDefaultList()` enumerates the enum),
named "AICopilot" via the localization fallback (`ResourceTranslator` falls back to `ToString()` when no resx key
exists — no crash, no missing-resource error).

## Architecture

```
AICopilotView (XAML)
  |
  v
AICopilotViewModel  ─── SendCommand / CancelCommand
  |
  v
AICopilotFactory.Create() → IAIConversationService   (Step 7 — provider-neutral)
  |
  v
IAgentToolLoop → ToolCallOrchestrator → DiagnosticEngine → Network Tools
  |
  v
AIAnalysisResponse → rendered as chat entries + findings
```

The view model contains **no** business logic: it forwards the typed user message to `IAIConversationService.SendAsync`
and projects the structured `AIAnalysisResponse` into display items. Everything below the view model is the already
tested Step 3–7 pipeline.

## State management

- `Entries` — observable chat history (`AICopilotChatEntry`: role, text, tool activity, findings).
- `Input` — the message box; cleared on send; read-only while busy.
- `IsBusy` — drives the loading indicator, disables Send, enables Cancel.
- `StatusMessage` — "Thinking..." / final conversation status / error text.
- `CancellationTokenSource` — one per request; `CancelCommand` cancels the in-flight provider call, tool call, and
  diagnostic workflow (cancellation propagates through the whole Step 5–7 chain).
- Conversation identity — a per-view `conversationId` keeps multi-turn context in the in-memory conversation store.

## Tool activity & evidence display

- While a request runs, the assistant entry shows a tool-activity line once tools have executed
  (e.g. *"AI ran 2 diagnostic tool call(s)."*, plus *"Tool-call limit reached."* when the Step 5 loop guard trips).
- Each structured `AIFinding` is rendered as a row: **Type** (Observation / Inference / Warning / Recommendation) +
  **Text** (+ evidence reference). Facts and inferences are visibly distinct — the UI never blends them.

## Provider

`AICopilotFactory` currently selects the deterministic `MockAIProvider` as a development placeholder. Wiring a real
provider or a `CustomAgentProvider` (runtime-supplied endpoint + auth, never a hard-coded key) is a configuration
change in the factory only — the view, view model, and all tests stay untouched.

## Testing

The WPF layer itself is Windows-only (built and verified on Windows CI). The conversation behavior behind the view
model is covered headlessly by `NETworkManager.AI.Tests` (`AICopilotConversationTests`): diagnostic-driven answers
contain observations + inference + recommendation, plain answers have no findings/tool activity, and cancellation is
user-friendly. 141/141 tests pass on Linux and Windows CI.

## Security

The copilot surface inherits every Step 3–7 guarantee: only registered read-only tools, policy/approval gates,
bounded tool-call rounds, sensitive-data redaction, and no shell/config access anywhere in the chain.
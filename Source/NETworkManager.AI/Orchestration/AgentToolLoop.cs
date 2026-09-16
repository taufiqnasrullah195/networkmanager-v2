using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Orchestration;

/// <summary>
///     Runs the bounded agent/tool loop: prompts a provider, executes any requested tool calls through the
///     orchestrator, and feeds the structured results back for the next round. Loop protection via maximum rounds,
///     a hard execution cap, and repeated-identical-call detection.
/// </summary>
public sealed class AgentToolLoop : IAgentToolLoop
{
    private readonly IAIProvider _provider;
    private readonly IToolCallOrchestrator _orchestrator;
    private readonly IToolRegistry _toolRegistry;
    private readonly AgentLoopOptions _options;
    private readonly IToolOrchestrationLogger _logger;

    public AgentToolLoop(
        IAIProvider provider,
        IToolCallOrchestrator orchestrator,
        IToolRegistry toolRegistry,
        AgentLoopOptions? options = null,
        IToolOrchestrationLogger? logger = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _options = options ?? new AgentLoopOptions();
        _logger = logger ?? NullToolOrchestrationLogger.Instance;
    }

    public async Task<AgentLoopResult> RunAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var toolResults = new List<AIToolResult>();
        var callSignatureCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var rounds = 0;
        var executions = 0;
        var conversationId = string.IsNullOrWhiteSpace(request.ConversationId)
            ? Guid.NewGuid().ToString("N")
            : request.ConversationId!;

        while (true)
        {
            var response = await _provider.SendAsync(BuildRoundRequest(request, conversationId, toolResults), cancellationToken).ConfigureAwait(false);

            if (response.ToolCalls is null || response.ToolCalls.Count == 0)
                return Done(response, toolResults, rounds, executions);

            foreach (var call in response.ToolCalls)
            {
                if (rounds >= _options.MaxToolRounds || executions >= _options.MaxToolExecutions)
                {
                    _logger.Log(ToolOrchestrationEvent.ToolCallLimitReached, new Dictionary<string, object?> { ["rounds"] = rounds, ["executions"] = executions });
                    return Done(response, toolResults, rounds, executions, limitReached: true,
                        errorCode: "ToolCallLimitReached", error: "Maximum tool-call rounds reached; no further tools executed.");
                }

                var signature = $"{call.ToolName}|{call.ArgumentsJson}";

                callSignatureCounts.TryGetValue(signature, out var count);
                count++;
                callSignatureCounts[signature] = count;

                if (count > _options.MaxRepeatedCalls)
                {
                    _logger.Log(ToolOrchestrationEvent.ToolCallLimitReached, new Dictionary<string, object?> { ["toolName"] = call.ToolName });
                    return Done(response, toolResults, rounds, executions, limitReached: true,
                        errorCode: "ToolCallLimitReached", error: $"Repeated identical tool call '{call.ToolName}' exceeded the limit.");
                }

                var context = new ToolExecutionContext { ConversationId = conversationId, ToolCallId = call.CallId };

                var outcome = await _orchestrator.ExecuteAsync(call, context, cancellationToken).ConfigureAwait(false);
                executions++;
                toolResults.Add(outcome.ToAIToolResult());
            }

            rounds++;
        }
    }

    private AIRequest BuildRoundRequest(AIRequest template, string conversationId, IReadOnlyList<AIToolResult> toolResults) => new()
    {
        SystemPrompt = template.SystemPrompt,
        UserPrompt = template.UserPrompt,
        Conversation = template.Conversation,
        ConversationId = conversationId,
        Tools = ToolDefinitionBuilder.BuildAll(_toolRegistry),
        ToolResults = toolResults.Count > 0 ? toolResults : null,
        Context = template.Context,
        Metadata = template.Metadata,
    };

    private static AgentLoopResult Done(AIResponse response, IReadOnlyList<AIToolResult> toolResults, int rounds, int executions,
        bool limitReached = false, string? errorCode = null, string? error = null) => new()
    {
        FinalResponse = response,
        ToolResults = toolResults,
        Rounds = rounds,
        Executions = executions,
        LimitReached = limitReached,
        ErrorCode = errorCode,
        Error = error,
    };
}
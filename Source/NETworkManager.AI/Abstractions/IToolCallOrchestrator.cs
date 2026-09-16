using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Bridge between AI reasoning and TheWiseNetwork tools: resolves a tool call through the registry, evaluates
///     policy and approval, then executes through the tool execution service and returns a structured outcome.
///     It never performs network operations itself, and it never invokes shells or executables directly.
/// </summary>
public interface IToolCallOrchestrator
{
    Task<ToolCallOutcome> ExecuteAsync(AIToolCall toolCall, ToolExecutionContext context, CancellationToken cancellationToken = default);

    /// <summary>Executes tool calls sequentially (order-preserving, bounded). Stopping on cancellation.</summary>
    Task<IReadOnlyList<ToolCallOutcome>> ExecuteAsync(IReadOnlyList<AIToolCall> toolCalls, ToolExecutionContext context, CancellationToken cancellationToken = default);
}
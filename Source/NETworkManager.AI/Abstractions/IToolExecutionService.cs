using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>Controlled execution boundary for network tools (resolve → validate → approve → execute → structured result).</summary>
public interface IToolExecutionService
{
    Task<ToolResult> ExecuteAsync(string toolName, object? input, ToolExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Executes a provider-neutral <see cref="AIToolCall"/>: deserializes its JSON arguments into the tool's typed
    ///     input, then runs the same resolve/validate/execute pipeline. Unknown tools are rejected; tools are never
    ///     created from a call.
    /// </summary>
    Task<ToolResult> ExecuteAsync(AIToolCall toolCall, ToolExecutionContext context, CancellationToken cancellationToken = default);
}
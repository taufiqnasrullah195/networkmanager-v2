using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Executes tools through a controlled boundary: resolve → validate → check approval → execute with timeout/cancellation → enrich.
///     Never executes arbitrary OS commands; only registered, typed tools.
/// </summary>
public interface IToolExecutionService
{
    /// <summary>
    ///     Executes the named tool with the given (boxed) typed input.
    ///     Returns a structured, auditable <see cref="ToolResult"/> — never a raw string.
    /// </summary>
    Task<ToolResult> ExecuteAsync(string toolName, object? input, ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}
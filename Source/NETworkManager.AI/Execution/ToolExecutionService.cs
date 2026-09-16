using System.Text.Json;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Execution;

/// <summary>
///     Controlled execution boundary: resolve → validate input type → approval gate → validate input → execute with
///     timeout/cancellation → enrich into an auditable <see cref="ToolResult"/>. Never executes arbitrary commands.
/// </summary>
public sealed class ToolExecutionService : IToolExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IToolRegistry _registry;

    public ToolExecutionService(IToolRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Task<ToolResult> ExecuteAsync(AIToolCall toolCall, ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(toolCall);

        var timestamp = DateTimeOffset.UtcNow;

        if (!_registry.TryGet(toolCall.ToolName, out var tool) || tool is null)
            return Task.FromResult(Fail(toolCall.ToolName, ToolRiskLevel.Low, timestamp, TimeSpan.Zero, requiresApproval: false,
                "ToolNotFound", $"No tool registered with the name '{toolCall.ToolName}'."));

        object? input = null;

        if (!string.IsNullOrWhiteSpace(toolCall.ArgumentsJson))
        {
            try
            {
                input = JsonSerializer.Deserialize(toolCall.ArgumentsJson, tool.InputType, JsonOptions);
            }
            catch (JsonException ex)
            {
                return Task.FromResult(Fail(toolCall.ToolName, tool.RiskLevel, timestamp, TimeSpan.Zero, tool.RequiresApproval,
                    "InvalidArguments", $"Arguments for '{toolCall.ToolName}' are not valid for '{tool.InputType.Name}': {ex.Message}"));
            }
            catch (NotSupportedException ex)
            {
                return Task.FromResult(Fail(toolCall.ToolName, tool.RiskLevel, timestamp, TimeSpan.Zero, tool.RequiresApproval,
                    "InvalidArguments", $"Arguments for '{toolCall.ToolName}' are not valid for '{tool.InputType.Name}': {ex.Message}"));
            }

            if (input is null)
                return Task.FromResult(Fail(toolCall.ToolName, tool.RiskLevel, timestamp, TimeSpan.Zero, tool.RequiresApproval,
                    "InvalidArguments", $"Arguments for '{toolCall.ToolName}' resolved to null."));
        }

        return ExecuteAsync(toolCall.ToolName, input, context, cancellationToken);
    }

    public async Task<ToolResult> ExecuteAsync(string toolName, object? input, ToolExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(toolName, out var tool) || tool is null)
            return Fail(toolName, ToolRiskLevel.Low, DateTimeOffset.UtcNow, TimeSpan.Zero, requiresApproval: false,
                "ToolNotFound", $"No tool registered with the name '{toolName}'.");

        // 1. Input type check
        if (input is not null && !tool.InputType.IsInstanceOfType(input))
            return Fail(toolName, tool.RiskLevel, DateTimeOffset.UtcNow, TimeSpan.Zero, tool.RequiresApproval,
                "InvalidInputType", $"Input must be of type '{tool.InputType.Name}'.");

        // 2. Approval gate (future HIGH/CRITICAL tools). Step 3 tools are read-only LOW -> RequiresApproval == false.
        if (tool.RequiresApproval && context.ApprovalGranted != true)
            return Fail(toolName, tool.RiskLevel, DateTimeOffset.UtcNow, TimeSpan.Zero, tool.RequiresApproval,
                "ApprovalRequired", $"Tool '{toolName}' requires approval.");

        // 3. Validation — never execute on invalid input
        if (input is IValidatableToolInput validatable)
        {
            var errors = validatable.Validate();
            if (errors.Count > 0)
                return Fail(toolName, tool.RiskLevel, DateTimeOffset.UtcNow, TimeSpan.Zero, tool.RequiresApproval,
                    "ValidationFailed", string.Join(" ", errors));
        }

        // 4. Execute with timeout + cancellation (outer bound guaranteed by WaitAsync; token also propagated so
        //    engines that honour CancellationToken stop promptly on timeout or caller cancel).
        var started = DateTimeOffset.UtcNow;

        using var timeoutCts = new CancellationTokenSource(tool.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var outcome = await tool.ExecuteAsync(input, context, linkedCts.Token)
                .WaitAsync(tool.Timeout, cancellationToken)
                .ConfigureAwait(false);

            return new ToolResult
            {
                ToolName = tool.Name,
                RiskLevel = tool.RiskLevel,
                RequiresApproval = tool.RequiresApproval,
                Success = outcome.Success,
                Data = outcome.Data,
                Error = outcome.Error,
                ErrorCode = outcome.ErrorCode,
                Timestamp = started,
                Duration = DateTimeOffset.UtcNow - started,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail(toolName, tool.RiskLevel, started, DateTimeOffset.UtcNow - started, tool.RequiresApproval,
                "Cancelled", $"Tool '{toolName}' was cancelled.");
        }
        catch (OperationCanceledException)
        {
            return Fail(toolName, tool.RiskLevel, started, DateTimeOffset.UtcNow - started, tool.RequiresApproval,
                "TimedOut", $"Tool '{toolName}' exceeded its timeout of {tool.Timeout.TotalSeconds:0.#}s.");
        }
        catch (TimeoutException)
        {
            return Fail(toolName, tool.RiskLevel, started, DateTimeOffset.UtcNow - started, tool.RequiresApproval,
                "TimedOut", $"Tool '{toolName}' exceeded its timeout of {tool.Timeout.TotalSeconds:0.#}s.");
        }
        catch (Exception ex)
        {
            return Fail(toolName, tool.RiskLevel, started, DateTimeOffset.UtcNow - started, tool.RequiresApproval,
                "ExecutionError", ex.Message);
        }
    }

    private static ToolResult Fail(string toolName, ToolRiskLevel risk, DateTimeOffset timestamp, TimeSpan duration,
        bool requiresApproval, string errorCode, string error) => new()
    {
        ToolName = toolName,
        RiskLevel = risk,
        RequiresApproval = requiresApproval,
        Success = false,
        Timestamp = timestamp,
        Duration = duration,
        ErrorCode = errorCode,
        Error = error,
    };
}
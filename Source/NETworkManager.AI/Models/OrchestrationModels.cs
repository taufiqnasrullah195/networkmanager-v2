namespace NETworkManager.AI.Models;

/// <summary>Policy decision for a tool call.</summary>
public enum PolicyDecision
{
    Allow = 0,
    Deny = 1,
    RequireApproval = 2,
}

/// <summary>Approval resolution after policy requires approval.</summary>
public enum ApprovalResult
{
    NotRequired = 0,
    Approved = 1,
    Rejected = 2,
    Required = 3,
}

/// <summary>Structured, secret-free orchestration lifecycle events (for logging/audit).</summary>
public enum ToolOrchestrationEvent
{
    ToolCallReceived,
    ToolResolved,
    ToolValidationFailed,
    PolicyDenied,
    ApprovalRequired,
    ToolExecutionStarted,
    ToolExecutionCompleted,
    ToolExecutionFailed,
    ToolCallLimitReached,
    ToolCallCancelled,
}

/// <summary>
///     Outcome of orchestrating one tool call: the audit picture (policy/approval/decision, timing) plus the
///     execution result when the tool actually ran. Never raw text; structured end to end.
/// </summary>
public sealed record ToolCallOutcome
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }

    /// <summary>Whether the underlying tool actually ran (false for not-found / deny / approval-required / invalid call).</summary>
    public required bool Executed { get; init; }

    public PolicyDecision? PolicyDecision { get; init; }
    public ApprovalResult? ApprovalResult { get; init; }

    /// <summary>Present when <see cref="Executed"/>; carries timing, risk, and the tool's structured evidence.</summary>
    public ToolResult? ToolResult { get; init; }

    /// <summary>Populated for non-executed outcomes.</summary>
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }

    public required DateTimeOffset Timestamp { get; init; }
    public required TimeSpan Duration { get; init; }

    public bool Success => ToolResult?.Success ?? false;
    public object? Data => ToolResult?.Data;

    public AIToolResult ToAIToolResult() => new()
    {
        CallId = CallId,
        ToolName = ToolName,
        Success = Success,
        Data = Data,
        Error = Error ?? ToolResult?.Error,
        ErrorCode = ErrorCode ?? ToolResult?.ErrorCode,
    };
}

/// <summary>Aggregate result of a bounded agent/tool loop.</summary>
public sealed record AgentLoopResult
{
    public required AIResponse FinalResponse { get; init; }
    public IReadOnlyList<AIToolResult> ToolResults { get; init; } = Array.Empty<AIToolResult>();
    public required int Rounds { get; init; }
    public required int Executions { get; init; }
    public bool LimitReached { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
}

/// <summary>Bounds for the agent/tool loop. Never unbounded.</summary>
public sealed record AgentLoopOptions
{
    /// <summary>Maximum provider round-trips that may request tool calls.</summary>
    public int MaxToolRounds { get; init; } = 5;

    /// <summary>Hard cap on total tool executions across the whole loop.</summary>
    public int MaxToolExecutions { get; init; } = 20;

    /// <summary>Maximum repeats of an identical (toolName + arguments) call before the loop stops.</summary>
    public int MaxRepeatedCalls { get; init; } = 3;
}
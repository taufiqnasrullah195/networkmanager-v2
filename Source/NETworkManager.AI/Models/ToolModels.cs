namespace NETworkManager.AI.Models;

/// <summary>
///     What a tool reports about its own execution: success plus data, or a failure with a machine-readable code.
///     The execution service wraps this into a <see cref="ToolResult"/> with timing/risk/identity metadata.
/// </summary>
public sealed record ToolOutcome
{
    public required bool Success { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static ToolOutcome Ok(object? data = null) => new() { Success = true, Data = data };

    public static ToolOutcome Failed(string errorCode, string error) =>
        new() { Success = false, ErrorCode = errorCode, Error = error };
}

/// <summary>
///     Context passed to every tool execution to support the future approval/audit boundary.
///     For LOW read-only tools <see cref="ApprovalGranted"/> is not required; future HIGH/CRITICAL tools will demand it.
/// </summary>
public sealed record ToolExecutionContext
{
    public string? User { get; init; }
    public string? ProviderId { get; init; }
    public string? AgentId { get; init; }
    public string? ConversationId { get; init; }
    public string? ToolCallId { get; init; }
    public bool? ApprovalGranted { get; init; }
    public string? ApprovalId { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

/// <summary>Typed tool inputs implement this so the execution service can validate before running.</summary>
public interface IValidatableToolInput
{
    IReadOnlyList<string> Validate();
}

/// <summary>
///     Structured, auditable result returned by the execution service. Never a raw string.
///     Carries enough information (tool, time, duration, outcome) to feed future audit logging.
/// </summary>
public sealed record ToolResult
{
    public required string ToolName { get; init; }
    public required ToolRiskLevel RiskLevel { get; init; }
    public required bool Success { get; init; }
    public bool RequiresApproval { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required TimeSpan Duration { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }
}
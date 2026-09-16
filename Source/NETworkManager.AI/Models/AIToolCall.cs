namespace NETworkManager.AI.Models;

/// <summary>A provider-neutral tool call: which tool, with which (JSON) arguments. Never implies execution by the provider.</summary>
public sealed record AIToolCall
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public required string ArgumentsJson { get; init; }
}

/// <summary>Structured evidence returned after a tool executes; sent back to the provider/agent for its next turn.</summary>
public sealed record AIToolResult
{
    public string? CallId { get; init; }
    public required string ToolName { get; init; }
    public required bool Success { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static AIToolResult FromToolResult(ToolResult result) => new()
    {
        ToolName = result.ToolName,
        Success = result.Success,
        Data = result.Data,
        Error = result.Error,
        ErrorCode = result.ErrorCode,
    };
}

/// <summary>A tool advertised to an AI provider (identity + a simple name→type input schema).</summary>
public sealed record AIToolDefinition
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string RiskLevel { get; init; }
    public required IReadOnlyDictionary<string, string> InputSchema { get; init; }
}
namespace NETworkManager.AI.Models;

/// <summary>Status of a tool execution as observed by the UI layer.</summary>
public enum ToolActivityStatus
{
    Started = 0,
    Completed = 1,
    Failed = 2,
    Cancelled = 3,
}

/// <summary>
///     Structured, safe tool-execution activity for the UI. Contains only non-sensitive metadata
///     (tool name, category, status, timing, optional summary) — never credentials, headers, or raw arguments.
/// </summary>
public sealed record ToolActivity
{
    /// <summary>Tool name from the actual tool metadata (never inferred from AI text).</summary>
    public required string ToolName { get; init; }

    /// <summary>Tool category from metadata, when known.</summary>
    public string? Category { get; init; }

    /// <summary>Diagnostic step id, when the activity comes from a workflow step.</summary>
    public string? StepId { get; init; }

    public required ToolActivityStatus Status { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public TimeSpan? Duration { get; init; }

    /// <summary>Short, safe human-readable summary (e.g. "skipped: dependency unavailable").</summary>
    public string? Summary { get; init; }
}
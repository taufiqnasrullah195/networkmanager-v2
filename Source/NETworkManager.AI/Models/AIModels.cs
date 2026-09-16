namespace NETworkManager.AI.Models;

/// <summary>A single conversation message (role + content).</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>Provider-neutral request to an AI provider. All fields optional; a provider uses what it supports.</summary>
public sealed record AIRequest
{
    public string? SystemPrompt { get; init; }
    public string? UserPrompt { get; init; }
    public IReadOnlyList<ChatMessage>? Conversation { get; init; }

    /// <summary>Tool definitions advertised to the provider (must originate from the tool registry, never remote input).</summary>
    public IReadOnlyList<AIToolDefinition>? Tools { get; init; }

    /// <summary>Structured evidence from tools already executed, returned to the provider for its next reasoning turn.</summary>
    public IReadOnlyList<AIToolResult>? ToolResults { get; init; }

    /// <summary>Explicit, controlled context (hostname, selected device, ...). Never secrets or credentials.</summary>
    public IReadOnlyDictionary<string, string>? Context { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>Optional conversation identity; the agent decides how to manage the state.</summary>
    public string? ConversationId { get; init; }
}

/// <summary>Provider-neutral response from an AI provider.</summary>
public sealed record AIResponse
{
    public required bool Success { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<AIToolCall>? ToolCalls { get; init; }
    public AIFinishReason FinishReason { get; init; } = AIFinishReason.Unknown;
    public string? Error { get; init; }
    public ProviderErrorCode? ErrorCode { get; init; }
    public AIUsage? Usage { get; init; }
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public static AIResponse Failed(ProviderErrorCode code, string error) =>
        new() { Success = false, ErrorCode = code, Error = error };
}

/// <summary>Why the provider stopped generating.</summary>
public enum AIFinishReason
{
    Unknown = 0,
    Stop = 1,
    Length = 2,
    ToolCalls = 3,
    ContentFiltered = 4,
}

/// <summary>Optional token-usage information; absent when a provider does not report it.</summary>
public sealed record AIUsage
{
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public int? TotalTokens { get; init; }
}
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Models;

/// <summary>A single chat message (provider-neutral role/content pair).</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>A prompt request to an <see cref="IAIProvider"/>.</summary>
public sealed record AIRequest
{
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
}

/// <summary>A tool invocation the AI is asking the host to perform on its behalf.</summary>
public sealed record ToolCallRequest(string ToolName, string Arguments);

/// <summary>A structured completion returned by an <see cref="IAIProvider"/>.</summary>
public sealed record AIResponse
{
    public required string Content { get; init; }
    public IReadOnlyList<ToolCallRequest>? ToolCalls { get; init; }
}

/// <summary>
///     Provider-neutral description of a tool for later conversion into a vendor function-calling schema.
///     Deliberately NOT in any vendor's format.
/// </summary>
public sealed record AIToolSchema
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string InputSchema { get; init; }
    public required string OutputSchema { get; init; }
}

/// <summary>Builds provider-neutral <see cref="AIToolSchema"/> instances from tool metadata.</summary>
public static class ToolSchemas
{
    public static AIToolSchema Build(INetworkTool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = tool.InputType.Name,
        OutputSchema = tool.OutputType.Name,
    };
}
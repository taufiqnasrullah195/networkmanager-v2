using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Provider-neutral abstraction for a future AI / LLM provider.
///     No provider-specific SDK types leak through this interface.
///     <para>
///         NOT IMPLEMENTED in this step — this is the seam the future AI layer will target.
///         Do not couple the core application to any specific vendor SDK.
///     </para>
/// </summary>
public interface IAIProvider
{
    /// <summary>Short, human-readable provider name (e.g. "OpenAI", "Anthropic", "Local").</summary>
    string Name { get; }

    /// <summary>Sends a prompt and returns a structured completion. May run indefinitely unless cancelled.</summary>
    Task<AIResponse> ChatAsync(AIRequest request, CancellationToken cancellationToken = default);

    /// <summary>Sends a prompt together with a set of provider-neutral tool schemas, enabling tool calls.</summary>
    Task<AIResponse> ChatWithToolsAsync(AIRequest request, IReadOnlyList<AIToolSchema> tools,
        CancellationToken cancellationToken = default);
}
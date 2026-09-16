using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Provider-neutral abstraction over an AI backend (cloud API, local LLM, or an external custom agent).
///     Implementations translate the neutral request/response models into their own protocol; the rest of the
///     application never sees a vendor SDK. Implementations must never expose credentials through this interface.
/// </summary>
public interface IAIProvider
{
    /// <summary>Capabilities this provider supports (chat, tool calling, streaming, ...).</summary>
    AIProviderCapability Capabilities { get; }

    /// <summary>Sends a request and returns a structured, provider-neutral response.</summary>
    Task<AIResponse> SendAsync(AIRequest request, CancellationToken cancellationToken = default);
}
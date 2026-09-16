using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Providers;

/// <summary>
///     Test/double provider: returns responses from a configurable handler instead of calling a real service.
///     Lets unit tests (and the app, for development) simulate success, tool calls, timeouts, and errors with no
///     external endpoint.
/// </summary>
public sealed class MockAIProvider : IAIProvider
{
    private readonly Func<AIRequest, CancellationToken, Task<AIResponse>> _handler;

    public AIProviderCapability Capabilities { get; init; } = AIProviderCapability.Chat | AIProviderCapability.ToolCalling;

    public MockAIProvider(Func<AIRequest, CancellationToken, Task<AIResponse>>? handler = null)
    {
        _handler = handler ?? ((_, _) => Task.FromResult(new AIResponse
        {
            Success = true,
            Text = "Mock response.",
            FinishReason = AIFinishReason.Stop,
        }));
    }

    public Task<AIResponse> SendAsync(AIRequest request, CancellationToken cancellationToken = default)
        => _handler(request, cancellationToken);
}
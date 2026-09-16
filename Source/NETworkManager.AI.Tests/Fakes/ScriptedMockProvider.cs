using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>
///     Scripted provider that returns responses from a queue (deterministic agent simulation for loop tests).
///     Once the queue is exhausted it returns a neutral "Done." final response.
/// </summary>
public sealed class ScriptedMockProvider : IAIProvider
{
    private readonly Queue<AIResponse> _responses;

    public AIProviderCapability Capabilities => AIProviderCapability.Chat | AIProviderCapability.ToolCalling;

    public int CallCount { get; private set; }

    public ScriptedMockProvider(params AIResponse[] responses)
    {
        if (responses.Length == 0)
            throw new ArgumentException("At least one response is required.", nameof(responses));

        _responses = new Queue<AIResponse>(responses);
    }

    public Task<AIResponse> SendAsync(AIRequest request, CancellationToken cancellationToken = default)
    {
        CallCount++;

        return Task.FromResult(_responses.Count > 0
            ? _responses.Dequeue()
            : new AIResponse { Success = true, Text = "Done.", FinishReason = AIFinishReason.Stop });
    }
}
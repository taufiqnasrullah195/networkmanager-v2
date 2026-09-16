using NETworkManager.AI.Models;

namespace NETworkManager.AI.Abstractions;

/// <summary>
///     Runs the agent/tool loop: prompts a provider, executes the provider's tool calls through the orchestrator,
///     and feeds structured results back for the next round — bounded by a maximum number of rounds (loop protection).
/// </summary>
public interface IAgentToolLoop
{
    Task<AgentLoopResult> RunAsync(AIRequest request, CancellationToken cancellationToken = default);
}
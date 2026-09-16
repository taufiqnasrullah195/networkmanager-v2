using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Conversation;
using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Orchestration;
using NETworkManager.AI.Providers;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tools;
using NETworkManager.Models;

namespace NETworkManager;

/// <summary>
///     Manual composition root for the AI copilot (Step 8). Follows the codebase's manual-DI convention: the
///     provider-neutral conversation service is assembled from the registry, execution service, orchestrator, loop,
///     diagnostic engine, and a provider. The default provider is the deterministic <see cref="MockAIProvider"/>
///     (development placeholder until a real provider / Custom Agent endpoint is configured at runtime).
/// </summary>
public static class AICopilotFactory
{
    public static IAIConversationService Create()
    {
        // Tool layer (Step 3): registered read-only diagnostic tools.
        var registry = new ToolRegistry();
        foreach (var tool in NetworkToolCollection.All())
            registry.Register(tool);

        // Diagnostic layer (Step 6): engine + workflow exposed as one LOW-risk tool.
        var execution = new ToolExecutionService(registry);
        var diagnostics = new DiagnosticEngine(execution);
        registry.Register(new InternetConnectivityDiagnosticTool(diagnostics));

        // Control layer (Step 5): orchestrator + bounded agent/tool loop.
        var orchestrator = new ToolCallOrchestrator(registry, execution);
        var loop = new AgentToolLoop(CreateProvider(), orchestrator, registry);

        // Conversation layer (Step 7).
        return new AIConversationService(loop, new DefaultDiagnosticAnalyzer());
    }

    /// <summary>
    ///     Provider selection. Returns the deterministic mock provider until a Custom Agent endpoint is configured
    ///     (runtime configuration of a real provider — never a hard-coded key — replaces this in a later step).
    /// </summary>
    private static IAIProvider CreateProvider() => new MockAIProvider();
}
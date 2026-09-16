using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Conversation;
using NETworkManager.AI.Models;
using NETworkManager.AI.Orchestration;
using NETworkManager.AI.Providers;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

/// <summary>
///     Step 8 tests: the AI copilot conversation surface is testable headlessly. These tests exercise the same
///     composition the WPF view model uses (provider -> loop -> orchestrator -> conversation service), without WPF.
/// </summary>
public class AICopilotConversationTests
{
    /// <summary>Builds the same composition as the UI factory, but with a scripted provider and fake tools.</summary>
    private static AIConversationService CreateCopilot(IAIProvider provider)
    {
        var registry = FakeNetwork.Registry(dnsOk: false);

        var execution = new NETworkManager.AI.Execution.ToolExecutionService(registry);
        var diagnostics = new NETworkManager.AI.Diagnostics.DiagnosticEngine(execution);
        registry.Register(new NETworkManager.AI.Diagnostics.InternetConnectivityDiagnosticTool(diagnostics));

        var orchestrator = new ToolCallOrchestrator(registry, execution);
        var loop = new AgentToolLoop(provider, orchestrator, registry);

        return new AIConversationService(loop, new NETworkManager.AI.Diagnostics.DefaultDiagnosticAnalyzer());
    }

    [Fact]
    public async Task Copilot_conversation_runs_diagnostic_and_returns_findings()
    {
        var provider = new MockAIProvider((request, _) =>
        {
            if (request.ToolResults is null || request.ToolResults.Count == 0)
                return Task.FromResult(new AIResponse
                {
                    Success = true,
                    FinishReason = AIFinishReason.ToolCalls,
                    ToolCalls = new[] { new AIToolCall { CallId = "d1", ToolName = "internet_connectivity_diagnostic", ArgumentsJson = "{}" } },
                });

            return Task.FromResult(new AIResponse
            {
                Success = true,
                Text = "Internet connectivity is unavailable; DNS resolution is failing.",
                FinishReason = AIFinishReason.Stop,
            });
        });

        var copilot = CreateCopilot(provider);

        var result = await copilot.SendAsync("Why is my Internet not working?");

        Assert.Equal(ConversationStatus.Completed, result.Status);
        Assert.Equal("Internet connectivity is unavailable; DNS resolution is failing.", result.Response.Summary);

        // The view model renders these: facts, inference, recommendation — all present.
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Observation);
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Inference);
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Recommendation);
        Assert.True(result.ToolExecutions > 0);
    }

    [Fact]
    public async Task Copilot_plain_answer_has_no_findings_or_tool_activity()
    {
        var copilot = CreateCopilot(new MockAIProvider());

        var result = await copilot.SendAsync("Hello");

        Assert.Equal(ConversationStatus.Completed, result.Status);
        Assert.Equal(0, result.ToolExecutions);
        Assert.Empty(result.Response.Findings);
        Assert.Equal(AIConfidence.Unknown, result.Response.Confidence);
    }

    [Fact]
    public async Task Copilot_cancellation_is_user_friendly()
    {
        var copilot = CreateCopilot(new MockAIProvider());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await copilot.SendAsync("Hello", cancellationToken: cts.Token);

        Assert.Equal(ConversationStatus.Cancelled, result.Status);
        Assert.Equal("The request was cancelled.", result.Response.Summary);
    }
}
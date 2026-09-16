using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Conversation;
using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Diagnostics.Workflows;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Orchestration;
using NETworkManager.AI.Providers;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

/// <summary>
///     Tests the testable core behind the WPF copilot view model: busy state, cancellation, status messages,
///     live tool activity events, error handling, and approval enforcement. All deterministic, no real network.
/// </summary>
public class CopilotControllerTests
{
    private static CopilotController BuildController(IAIProvider provider, ToolRegistry? registry = null)
    {
        // dnsOk: false — the standard failing-DNS scenario the copilot tests assert against.
        registry ??= FakeNetwork.Registry(dnsOk: false);

        var notifier = new ToolActivityNotifier();
        var execution = new ToolExecutionService(registry);
        var diagnostics = new DiagnosticEngine(execution, notifier);
        registry.Register(new InternetConnectivityDiagnosticTool(diagnostics));

        var orchestrator = new ToolCallOrchestrator(registry, execution, logger: notifier);
        var loop = new AgentToolLoop(provider, orchestrator, registry, logger: notifier);
        var service = new AIConversationService(loop, new DefaultDiagnosticAnalyzer());

        return new CopilotController(service, notifier);
    }

    private static MockAIProvider DiagnosticProvider() => new((request, _) =>
    {
        if (request.ToolResults is null || request.ToolResults.Count == 0)
            return Task.FromResult(new AIResponse
            {
                Success = true,
                FinishReason = AIFinishReason.ToolCalls,
                ToolCalls = new[] { new AIToolCall { CallId = "d1", ToolName = "internet_connectivity_diagnostic", ArgumentsJson = "{}" } },
            });

        return Task.FromResult(new AIResponse { Success = true, Text = "DNS resolution is failing.", FinishReason = AIFinishReason.Stop });
    });

    [Fact]
    public async Task Send_returns_response_and_busy_state_transitions()
    {
        var gate = new TaskCompletionSource<AIResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new MockAIProvider((_, _) => gate.Task);
        var controller = BuildController(provider);

        var sendTask = controller.SendAsync("Hello");
        await Task.Yield();

        Assert.True(controller.IsBusy); // loading state starts

        gate.SetResult(new AIResponse { Success = true, Text = "Hi.", FinishReason = AIFinishReason.Stop });
        var result = await sendTask;

        Assert.False(controller.IsBusy); // loading state ends
        Assert.Equal(ConversationStatus.Completed, result.Status);
        Assert.Equal("Hi.", result.Response.Summary);
    }

    [Fact]
    public async Task Empty_message_is_rejected()
    {
        var controller = BuildController(new MockAIProvider());

        await Assert.ThrowsAsync<ArgumentException>(() => controller.SendAsync("  "));
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Concurrent_second_message_is_rejected()
    {
        var gate = new TaskCompletionSource<AIResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new MockAIProvider((_, _) => gate.Task);
        var controller = BuildController(provider);

        var sendTask = controller.SendAsync("first");
        await Task.Yield();

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SendAsync("second"));

        gate.SetResult(new AIResponse { Success = true, Text = "ok", FinishReason = AIFinishReason.Stop });
        await sendTask;
    }

    [Fact]
    public async Task Cancellation_returns_cancelled_result_and_clears_busy_state()
    {
        var gate = new TaskCompletionSource<AIResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new MockAIProvider((_, ct) =>
        {
            ct.Register(() => gate.TrySetCanceled(ct));
            return gate.Task;
        });
        var controller = BuildController(provider);

        var sendTask = controller.SendAsync("Why is my internet not working?");
        await Task.Yield();

        controller.Cancel();

        var result = await sendTask;

        Assert.Equal(ConversationStatus.Cancelled, result.Status);
        Assert.False(controller.IsBusy); // UI never stuck in loading state
    }

    [Fact]
    public async Task Provider_error_is_surfaced_as_friendly_failure()
    {
        var provider = new MockAIProvider((_, _) =>
            throw new ProviderException(ProviderErrorCode.ProviderUnavailable, "connection refused 127.0.0.1:443"));
        var controller = BuildController(provider);

        var result = await controller.SendAsync("Hello");

        Assert.Equal(ConversationStatus.Failed, result.Status);
        Assert.Contains("unavailable", result.Response.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", result.Response.Summary, StringComparison.Ordinal); // no raw internals in UI text
    }

    [Fact]
    public async Task Authentication_error_is_surfaced_as_friendly_failure()
    {
        var provider = new MockAIProvider((_, _) =>
            throw new ProviderException(ProviderErrorCode.AuthenticationFailed, "401"));
        var controller = BuildController(provider);

        var result = await controller.SendAsync("Hello");

        Assert.Equal(ConversationStatus.Failed, result.Status);
        Assert.Contains("Authentication", result.Response.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeout_is_surfaced_as_friendly_failure()
    {
        var provider = new MockAIProvider((_, _) =>
            throw new ProviderException(ProviderErrorCode.Timeout, "took too long"));
        var controller = BuildController(provider);

        var result = await controller.SendAsync("Hello");

        Assert.Equal(ConversationStatus.Failed, result.Status);
        Assert.Contains("timed out", result.Response.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Tool_activity_events_reach_the_ui_layer()
    {
        var activities = new List<ToolActivity>();
        var controller = BuildController(DiagnosticProvider());

        controller.ToolActivity += (_, activity) => activities.Add(activity);

        var result = await controller.SendAsync("Why is my internet not working?");

        Assert.Equal(ConversationStatus.Completed, result.Status);
        Assert.Contains(activities, a => a.Status == ToolActivityStatus.Started && a.ToolName == "internet_connectivity_diagnostic");
        Assert.Contains(activities, a => a.Status == ToolActivityStatus.Completed && a.ToolName == "internet_connectivity_diagnostic");
        // diagnostic workflow steps also surface as activity with their actual tool names
        Assert.Contains(activities, a => a.Status == ToolActivityStatus.Started && a.ToolName == "network_adapter_info");
        Assert.Contains(activities, a => a.Status == ToolActivityStatus.Failed && a.ToolName == "dns_lookup"); // dnsOk=false registry
    }

    [Fact]
    public async Task Tool_failure_emits_failed_activity()
    {
        var registry = new ToolRegistry();
        var executed = 0;
        registry.Register(new FakeNetworkTool
        {
            Name = "failing_tool",
            InputType = typeof(EchoInput),
            Handler = (input, _, _) => { executed++; return Task.FromResult(ToolOutcome.Failed("Boom", "tool exploded")); },
        });

        var provider = new MockAIProvider((request, _) =>
        {
            if (request.ToolResults is null || request.ToolResults.Count == 0)
                return Task.FromResult(new AIResponse
                {
                    Success = true,
                    FinishReason = AIFinishReason.ToolCalls,
                    ToolCalls = new[] { new AIToolCall { CallId = "f1", ToolName = "failing_tool", ArgumentsJson = """{"message":"x"}""" } },
                });

            return Task.FromResult(new AIResponse { Success = true, Text = "The tool failed.", FinishReason = AIFinishReason.Stop });
        });

        var controller = BuildController(provider, registry);
        var activities = new List<ToolActivity>();
        controller.ToolActivity += (_, activity) => activities.Add(activity);

        var result = await controller.SendAsync("run the tool");

        Assert.Equal(1, executed);
        Assert.Contains(activities, a => a.Status == ToolActivityStatus.Failed && a.ToolName == "failing_tool" && a.Summary == "Boom");
        Assert.Contains(activities, a => a.Status == ToolActivityStatus.Started && a.ToolName == "failing_tool");
    }

    [Fact]
    public async Task Evidence_is_structured_not_fabricated_from_text()
    {
        var controller = BuildController(DiagnosticProvider());

        var result = await controller.SendAsync("Why is my internet not working?");

        var evidenceItems = CopilotEvidenceProjection.Project(result.Response);

        Assert.NotEmpty(evidenceItems);
        Assert.Contains(evidenceItems, i => i.Glyph == "✗" && i.Title == "dns_lookup");
        Assert.Contains(evidenceItems, i => i.Glyph == "✓" && i.Title == "gateway_ping");
        Assert.All(evidenceItems, i => Assert.Contains("Tool: ", i.Detail, StringComparison.Ordinal)); // provenance present
    }

    [Fact]
    public async Task Text_only_response_produces_no_evidence_items()
    {
        var provider = new MockAIProvider((_, _) => Task.FromResult(new AIResponse
        {
            Success = true,
            Text = "The ping to 8.8.8.8 failed and DNS is broken, trust me.",
            FinishReason = AIFinishReason.Stop,
        }));
        var controller = BuildController(provider);

        var result = await controller.SendAsync("Is my network down?");

        var evidenceItems = CopilotEvidenceProjection.Project(result.Response);

        Assert.Empty(evidenceItems); // AI text can never fabricate evidence in the UI
    }

    [Fact]
    public async Task Approval_is_not_bypassed_for_medium_risk_tools()
    {
        var registry = new ToolRegistry();
        var executed = 0;
        registry.Register(new FakeNetworkTool
        {
            Name = "risky_tool",
            RiskLevel = ToolRiskLevel.Medium,
            InputType = typeof(EchoInput),
            Handler = (input, _, _) => { executed++; return Task.FromResult(ToolOutcome.Ok()); },
        });

        var provider = new MockAIProvider((request, _) =>
        {
            if (request.ToolResults is null || request.ToolResults.Count == 0)
                return Task.FromResult(new AIResponse
                {
                    Success = true,
                    FinishReason = AIFinishReason.ToolCalls,
                    ToolCalls = new[] { new AIToolCall { CallId = "r1", ToolName = "risky_tool", ArgumentsJson = """{"message":"x"}""" } },
                });

            return Task.FromResult(new AIResponse { Success = true, Text = "Approval was required.", FinishReason = AIFinishReason.Stop });
        });

        var controller = BuildController(provider, registry);

        var result = await controller.SendAsync("run the risky tool");

        Assert.Equal(0, executed); // policy blocked execution — nothing ran
        Assert.Contains(result.Response.Findings, f => f.Title.Contains("requires approval", StringComparison.OrdinalIgnoreCase));
    }
}
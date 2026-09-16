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

public class AIConversationServiceTests
{
    private static AIConversationService CreateService(IAIProvider provider, ToolRegistry? registry = null, int maxContextMessages = 20)
    {
        registry ??= FakeNetwork.Registry();
        var execution = new ToolExecutionService(registry);
        var engine = new DiagnosticEngine(execution);
        registry.Register(new InternetConnectivityDiagnosticTool(engine));

        var orchestrator = new ToolCallOrchestrator(registry, execution);
        var loop = new AgentToolLoop(provider, orchestrator, registry);

        return new AIConversationService(loop, new DefaultDiagnosticAnalyzer(), maxContextMessages: maxContextMessages);
    }

    private static MockAIProvider TextProvider(string text) => new((_, _) =>
        Task.FromResult(new AIResponse { Success = true, Text = text, FinishReason = AIFinishReason.Stop }));

    private static MockAIProvider DiagnosticProvider(string finalText) => new((request, _) =>
    {
        if (request.ToolResults is null || request.ToolResults.Count == 0)
            return Task.FromResult(new AIResponse
            {
                Success = true,
                FinishReason = AIFinishReason.ToolCalls,
                ToolCalls = new[] { new AIToolCall { CallId = "diag-1", ToolName = "internet_connectivity_diagnostic", ArgumentsJson = "{}" } },
            });

        return Task.FromResult(new AIResponse { Success = true, Text = finalText, FinishReason = AIFinishReason.Stop });
    });

    [Fact]
    public async Task New_conversation_gets_an_id_and_completes()
    {
        var service = CreateService(TextProvider("Hello."));

        var result = await service.SendAsync("Hello");

        Assert.False(string.IsNullOrWhiteSpace(result.ConversationId));
        Assert.Equal(ConversationStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Scenario_A_plain_response_without_tools_has_unknown_confidence()
    {
        var service = CreateService(TextProvider("Hello."));

        var result = await service.SendAsync("Hello");

        Assert.Equal("Hello.", result.Response.Summary);
        Assert.Equal(AIConfidence.Unknown, result.Response.Confidence);
        Assert.Empty(result.Response.Findings);
        Assert.Empty(result.Response.EvidenceContexts);
    }

    [Fact]
    public async Task Scenario_B_diagnostic_produces_evidence_backed_response()
    {
        var service = CreateService(DiagnosticProvider("Internet connectivity is unavailable; DNS resolution is failing."), FakeNetwork.Registry(dnsOk: false));

        var result = await service.SendAsync("Why is my Internet not working?");

        Assert.Equal(ConversationStatus.Completed, result.Status);
        Assert.Equal(AIConfidence.Supported, result.Response.Confidence);
        Assert.True(result.Rounds >= 1);

        // facts (observations) from evidence
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Observation && f.Severity == AIFindingSeverity.High);
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Observation && f.Confidence == AIConfidence.Confirmed);

        // inference + recommendation
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Inference && f.Title.Contains("DNS resolution failed", StringComparison.Ordinal));
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Recommendation);

        // evidence context + provenance
        var context = Assert.Single(result.Response.EvidenceContexts);
        Assert.Equal(FailureClass.DnsFailure, context.Classification);
        Assert.Contains(context.Evidence, e => e.StepId == "dns_lookup" && !e.Success);
        Assert.NotEmpty(result.Response.EvidenceReferences);
        Assert.NotEmpty(result.Response.PossibleCauses);
    }

    [Fact]
    public async Task Scenario_C_unknown_tool_is_rejected_without_diagnostic()
    {
        var provider = new MockAIProvider((request, _) =>
        {
            if (request.ToolResults is null || request.ToolResults.Count == 0)
                return Task.FromResult(new AIResponse
                {
                    Success = true,
                    FinishReason = AIFinishReason.ToolCalls,
                    ToolCalls = new[] { new AIToolCall { CallId = "x", ToolName = "run_nmap", ArgumentsJson = "{}" } },
                });

            return Task.FromResult(new AIResponse { Success = true, Text = "I could not run that tool.", FinishReason = AIFinishReason.Stop });
        });

        var service = CreateService(provider);

        var result = await service.SendAsync("Scan the network");

        Assert.Equal(ConversationStatus.Completed, result.Status);
        Assert.Contains(result.Response.Findings, f => f.Type == AIFindingType.Warning && f.Title.Contains("run_nmap", StringComparison.Ordinal));
        Assert.Empty(result.Response.EvidenceContexts); // no diagnostic workflow ran
    }

    [Fact]
    public async Task Scenario_D_repeated_tools_hit_loop_limit()
    {
        var provider = new MockAIProvider((_, _) => Task.FromResult(new AIResponse
        {
            Success = true,
            FinishReason = AIFinishReason.ToolCalls,
            ToolCalls = new[] { new AIToolCall { CallId = "r", ToolName = "internet_connectivity_diagnostic", ArgumentsJson = "{}" } },
        }));

        var service = CreateService(provider);

        var result = await service.SendAsync("check");

        Assert.True(result.ToolCallLimitReached);
        Assert.Equal(3, result.ToolExecutions); // MaxRepeatedCalls defaults to 3
    }

    [Fact]
    public async Task Scenario_E_provider_unavailable_returns_friendly_error()
    {
        var provider = new MockAIProvider((_, _) => throw new ProviderException(ProviderErrorCode.ProviderUnavailable, "provider down"));
        var service = CreateService(provider);

        var result = await service.SendAsync("Hello");

        Assert.Equal(ConversationStatus.Failed, result.Status);
        Assert.Contains("unavailable", result.Response.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("down", result.Response.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_timeout_returns_friendly_error()
    {
        var provider = new MockAIProvider((_, _) => throw new ProviderException(ProviderErrorCode.Timeout, "slow"));
        var service = CreateService(provider);

        var result = await service.SendAsync("Hello");

        Assert.Equal(ConversationStatus.Failed, result.Status);
        Assert.Contains("timed out", result.Response.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Malformed_provider_response_returns_friendly_error()
    {
        var provider = new MockAIProvider((_, _) => Task.FromResult(new AIResponse { Success = false, ErrorCode = ProviderErrorCode.InvalidResponse, Error = "bad" }));
        var service = CreateService(provider);

        var result = await service.SendAsync("Hello");

        Assert.Equal(ConversationStatus.Failed, result.Status);
        Assert.Contains("could not process", result.Response.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancellation_returns_cancelled_status()
    {
        var service = CreateService(TextProvider("Hello."));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.SendAsync("Hello", cancellationToken: cts.Token);

        Assert.Equal(ConversationStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Missing_evidence_is_not_fabricated()
    {
        var service = CreateService(TextProvider("I cannot tell without running a diagnostic."));

        var result = await service.SendAsync("Is my network down?");

        Assert.Equal(AIConfidence.Unknown, result.Response.Confidence);
        Assert.Empty(result.Response.EvidenceContexts);
        Assert.DoesNotContain(result.Response.Findings, f => f.Type == AIFindingType.Observation);
    }

    [Fact]
    public async Task Sensitive_data_is_redacted_before_sending()
    {
        string? capturedUserPrompt = null;
        var provider = new MockAIProvider((request, _) =>
        {
            capturedUserPrompt = request.UserPrompt;
            return Task.FromResult(new AIResponse { Success = true, Text = "ok", FinishReason = AIFinishReason.Stop });
        });
        var service = CreateService(provider);

        _ = await service.SendAsync("My password=hunter2 and token=abc123, please check connectivity.");

        Assert.NotNull(capturedUserPrompt);
        Assert.DoesNotContain("hunter2", capturedUserPrompt);
        Assert.DoesNotContain("abc123", capturedUserPrompt);
        Assert.Contains("password=", capturedUserPrompt);
    }

    [Fact]
    public async Task Multi_turn_conversation_preserves_history()
    {
        IReadOnlyList<ChatMessage>? capturedHistory = null;
        var provider = new MockAIProvider((request, _) =>
        {
            capturedHistory ??= request.Conversation;
            return Task.FromResult(new AIResponse { Success = true, Text = "ok", FinishReason = AIFinishReason.Stop });
        });

        var service = CreateService(provider);

        var first = await service.SendAsync("First message");
        capturedHistory = null;

        _ = await service.SendAsync("Second message", first.ConversationId);

        Assert.NotNull(capturedHistory);
        Assert.Contains(capturedHistory, m => m.Role == "user" && m.Content == "First message");
    }
}
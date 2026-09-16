using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Orchestration;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class AgentToolLoopTests
{
    private static AIResponse PingResponse() => new()
    {
        Success = true,
        Text = "I need to test connectivity.",
        FinishReason = AIFinishReason.ToolCalls,
        ToolCalls = new[] { new AIToolCall { CallId = "call-1", ToolName = "ping", ArgumentsJson = """{"target":"10.0.0.1"}""" } },
    };

    private static AIResponse FinalResponse() => new()
    {
        Success = true,
        Text = "Diagnosis complete.",
        FinishReason = AIFinishReason.Stop,
    };

    private static ToolRegistry RegistryWithPing()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool
        {
            Name = "ping",
            InputType = typeof(PingInput),
            Handler = (input, _, _) => Task.FromResult(ToolOutcome.Ok(input)),
        });
        return registry;
    }

    private static AgentToolLoop CreateLoop(IToolRegistry registry, IAIProvider provider, AgentLoopOptions? options = null)
    {
        var orchestrator = new ToolCallOrchestrator(registry, new ToolExecutionService(registry));
        return new AgentToolLoop(provider, orchestrator, registry, options);
    }

    [Fact]
    public async Task Final_response_without_tools_returns_directly()
    {
        var loop = CreateLoop(RegistryWithPing(), new ScriptedMockProvider(FinalResponse()));

        var result = await loop.RunAsync(new AIRequest { UserPrompt = "check" });

        Assert.False(result.LimitReached);
        Assert.Equal(0, result.Rounds);
        Assert.Equal(0, result.Executions);
        Assert.Equal("Diagnosis complete.", result.FinalResponse.Text);
    }

    [Fact]
    public async Task One_tool_call_then_final_response()
    {
        var loop = CreateLoop(RegistryWithPing(), new ScriptedMockProvider(PingResponse(), FinalResponse()));

        var result = await loop.RunAsync(new AIRequest { UserPrompt = "check" });

        Assert.Equal(1, result.Rounds);
        Assert.Equal(1, result.Executions);
        Assert.False(result.LimitReached);
        Assert.Equal("Diagnosis complete.", result.FinalResponse.Text);

        var toolResult = Assert.Single(result.ToolResults);
        Assert.Equal("ping", toolResult.ToolName);
        Assert.True(toolResult.Success);
    }

    [Fact]
    public async Task Multiple_tool_calls_in_one_response_are_all_executed()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool
        {
            Name = "ping",
            InputType = typeof(PingInput),
            Handler = (input, _, _) => Task.FromResult(ToolOutcome.Ok(input)),
        });
        registry.Register(new EchoTestTool());

        var response = new AIResponse
        {
            Success = true,
            FinishReason = AIFinishReason.ToolCalls,
            ToolCalls = new[]
            {
                new AIToolCall { CallId = "c1", ToolName = "ping", ArgumentsJson = """{"target":"10.0.0.1"}""" },
                new AIToolCall { CallId = "c2", ToolName = "echo_test", ArgumentsJson = """{"message":"hi"}""" },
            },
        };

        var loop = CreateLoop(registry, new ScriptedMockProvider(response, FinalResponse()));

        var result = await loop.RunAsync(new AIRequest { UserPrompt = "check" });

        Assert.Equal(2, result.Executions);
        Assert.Equal(2, result.ToolResults.Count);
        Assert.Equal("c1", result.ToolResults[0].CallId);
        Assert.Equal("c2", result.ToolResults[1].CallId);
    }

    [Fact]
    public async Task Unknown_tool_requested_by_agent_is_rejected_with_evidence()
    {
        var response = new AIResponse
        {
            Success = true,
            FinishReason = AIFinishReason.ToolCalls,
            ToolCalls = new[] { new AIToolCall { CallId = "c1", ToolName = "execute_powershell", ArgumentsJson = "{}" } },
        };

        var loop = CreateLoop(RegistryWithPing(), new ScriptedMockProvider(response, FinalResponse()));

        var result = await loop.RunAsync(new AIRequest { UserPrompt = "check" });

        var toolResult = Assert.Single(result.ToolResults);
        Assert.Equal("ToolNotFound", toolResult.ErrorCode);
        Assert.False(toolResult.Success);
    }

    [Fact]
    public async Task Maximum_tool_rounds_prevents_infinite_loop()
    {
        var pingResponses = Enumerable.Repeat(PingResponse(), 10).ToArray();
        var loop = CreateLoop(RegistryWithPing(), new ScriptedMockProvider(pingResponses), new AgentLoopOptions { MaxToolRounds = 3 });

        var result = await loop.RunAsync(new AIRequest { UserPrompt = "check" });

        Assert.True(result.LimitReached);
        Assert.Equal("ToolCallLimitReached", result.ErrorCode);
        Assert.Equal(3, result.Executions);
        Assert.Equal(3, result.Rounds);
    }

    [Fact]
    public async Task Repeated_identical_tool_call_is_stopped()
    {
        var pingResponses = Enumerable.Repeat(PingResponse(), 10).ToArray();

        // MaxToolRounds high so the repeated-call guard triggers first (default MaxRepeatedCalls = 3).
        var loop = CreateLoop(RegistryWithPing(), new ScriptedMockProvider(pingResponses), new AgentLoopOptions { MaxToolRounds = 100, MaxToolExecutions = 100 });

        var result = await loop.RunAsync(new AIRequest { UserPrompt = "check" });

        Assert.True(result.LimitReached);
        Assert.Equal("ToolCallLimitReached", result.ErrorCode);
        Assert.Equal(3, result.Executions); // the 4th identical call is stopped before execution
    }

    [Fact]
    public async Task Cancellation_propagates_to_the_loop()
    {
        var loop = CreateLoop(RegistryWithPing(), new ScriptedMockProvider(PingResponse(), FinalResponse()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Provider returns synchronously; the orchestrator honours the token and returns Cancelled.
        var result = await loop.RunAsync(new AIRequest { UserPrompt = "check" }, cts.Token);

        Assert.False(result.LimitReached);
        Assert.Equal("Cancelled", result.ToolResults[0].ErrorCode);
    }
}
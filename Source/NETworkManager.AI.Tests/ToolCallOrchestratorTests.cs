using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Orchestration;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolCallOrchestratorTests
{
    private static ToolCallOrchestrator Create(params INetworkTool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
            registry.Register(tool);

        return new ToolCallOrchestrator(registry, new ToolExecutionService(registry));
    }

    private static AIToolCall Call(string toolName, string argumentsJson = "{}", string callId = "call-1") =>
        new() { CallId = callId, ToolName = toolName, ArgumentsJson = argumentsJson };

    [Fact]
    public async Task Valid_tool_call_executes_and_preserves_structure()
    {
        var orchestrator = Create(new EchoTestTool());

        var outcome = await orchestrator.ExecuteAsync(Call("echo_test", """{"message":"hello"}"""), new ToolExecutionContext());

        Assert.True(outcome.Executed);
        Assert.True(outcome.Success);
        Assert.Equal("call-1", outcome.CallId);
        Assert.Equal(PolicyDecision.Allow, outcome.PolicyDecision);
        Assert.Equal(ApprovalResult.NotRequired, outcome.ApprovalResult);

        var echo = Assert.IsType<EchoOutput>(outcome.Data);
        Assert.Equal("hello", echo.Message);

        var feedback = outcome.ToAIToolResult();
        Assert.Equal("call-1", feedback.CallId);
        Assert.Equal("echo_test", feedback.ToolName);
        Assert.True(feedback.Success);
    }

    [Fact]
    public async Task Unknown_tool_is_rejected_without_execution()
    {
        var executed = 0;
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool
        {
            Name = "ping",
            InputType = typeof(PingInput),
            Handler = (input, _, _) => { executed++; return Task.FromResult(ToolOutcome.Ok(input)); },
        });

        var orchestrator = new ToolCallOrchestrator(registry, new ToolExecutionService(registry));

        var outcome = await orchestrator.ExecuteAsync(Call("execute_powershell"), new ToolExecutionContext());

        Assert.False(outcome.Executed);
        Assert.False(outcome.Success);
        Assert.Equal("ToolNotFound", outcome.ErrorCode);
        Assert.Equal(0, executed); // nothing ran — arbitrary commands are impossible
    }

    [Fact]
    public async Task Invalid_json_arguments_are_rejected()
    {
        var orchestrator = Create(new EchoTestTool());

        var outcome = await orchestrator.ExecuteAsync(Call("echo_test", """{not-json}"""), new ToolExecutionContext());

        Assert.Equal("InvalidArguments", outcome.ToAIToolResult().ErrorCode);
        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task Missing_required_argument_is_rejected()
    {
        var orchestrator = Create(new EchoTestTool());

        var outcome = await orchestrator.ExecuteAsync(Call("echo_test", "{}"), new ToolExecutionContext());

        Assert.Equal("ValidationFailed", outcome.ToAIToolResult().ErrorCode);
        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task Wrong_argument_type_is_rejected()
    {
        var orchestrator = Create(new FakeNetworkTool { Name = "ping", InputType = typeof(PingInput) });

        var outcome = await orchestrator.ExecuteAsync(Call("ping", """{"target":123}"""), new ToolExecutionContext());

        Assert.Equal("InvalidArguments", outcome.ToAIToolResult().ErrorCode);
        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task Out_of_range_argument_is_rejected()
    {
        var orchestrator = Create(new FakeNetworkTool { Name = "ping", InputType = typeof(PingInput) });

        var outcome = await orchestrator.ExecuteAsync(Call("ping", """{"target":"x","count":1000}"""), new ToolExecutionContext());

        Assert.Equal("ValidationFailed", outcome.ToAIToolResult().ErrorCode);
        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task Executing_tool_reports_execution_failure()
    {
        var orchestrator = Create(new FakeNetworkTool
        {
            Name = "ping",
            InputType = typeof(PingInput),
            Handler = (_, _, _) => Task.FromResult(ToolOutcome.Failed("boom", "Something failed.")),
        });

        var outcome = await orchestrator.ExecuteAsync(Call("ping", """{"target":"x","count":4}"""), new ToolExecutionContext());

        Assert.True(outcome.Executed);
        Assert.False(outcome.Success);
        Assert.Equal("boom", outcome.ToAIToolResult().ErrorCode);
    }

    [Fact]
    public async Task Policy_deny_prevents_execution()
    {
        var executed = 0;
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool
        {
            Name = "ping",
            InputType = typeof(PingInput),
            Handler = (input, _, _) => { executed++; return Task.FromResult(ToolOutcome.Ok(input)); },
        });

        var orchestrator = new ToolCallOrchestrator(
            registry,
            new ToolExecutionService(registry),
            policy: new DenyEverythingPolicy());

        var outcome = await orchestrator.ExecuteAsync(Call("ping"), new ToolExecutionContext());

        Assert.False(outcome.Executed);
        Assert.Equal(PolicyDecision.Deny, outcome.PolicyDecision);
        Assert.Equal("PolicyDenied", outcome.ErrorCode);
        Assert.Equal(0, executed);
    }

    [Fact]
    public async Task Policy_require_approval_without_approval_blocks_execution()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool { Name = "ping", InputType = typeof(PingInput) });

        var orchestrator = new ToolCallOrchestrator(
            registry,
            new ToolExecutionService(registry),
            policy: new RequireApprovalPolicy());

        var outcome = await orchestrator.ExecuteAsync(Call("ping"), new ToolExecutionContext());

        Assert.False(outcome.Executed);
        Assert.Equal("ApprovalRequired", outcome.ErrorCode);
    }

    [Fact]
    public async Task Malformed_tool_call_missing_name_is_rejected()
    {
        var orchestrator = Create(new EchoTestTool());

        var outcome = await orchestrator.ExecuteAsync(new AIToolCall { CallId = "c1", ToolName = " ", ArgumentsJson = "{}" }, new ToolExecutionContext());

        Assert.False(outcome.Executed);
        Assert.Equal("InvalidCall", outcome.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_stops_execution()
    {
        var orchestrator = Create(new EchoTestTool());

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await orchestrator.ExecuteAsync(Call("echo_test", """{"message":"x"}"""), new ToolExecutionContext(), cts.Token);

        Assert.Equal("Cancelled", outcome.ToAIToolResult().ErrorCode);
        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task Tool_timeout_is_enforced()
    {
        var orchestrator = Create(new FakeNetworkTool
        {
            Name = "slow",
            InputType = typeof(EchoInput),
            Timeout = TimeSpan.FromMilliseconds(100),
            Handler = async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                return ToolOutcome.Ok();
            },
        });

        var outcome = await orchestrator.ExecuteAsync(Call("slow", """{"message":"x"}"""), new ToolExecutionContext());

        Assert.Equal("TimedOut", outcome.ToAIToolResult().ErrorCode);
        Assert.False(outcome.Success);
    }

    [Fact]
    public async Task Multiple_tool_calls_execute_in_order_and_preserve_call_ids()
    {
        var orchestrator = Create(new EchoTestTool());

        var outcomes = await orchestrator.ExecuteAsync(new[]
        {
            Call("echo_test", """{"message":"one"}""", "call-1"),
            Call("echo_test", """{"message":"two"}""", "call-2"),
        }, new ToolExecutionContext());

        Assert.Equal(2, outcomes.Count);
        Assert.Equal("call-1", outcomes[0].CallId);
        Assert.Equal("call-2", outcomes[1].CallId);

        var first = Assert.IsType<EchoOutput>(outcomes[0].Data);
        var second = Assert.IsType<EchoOutput>(outcomes[1].Data);
        Assert.Equal("one", first.Message);
        Assert.Equal("two", second.Message);
    }

    private sealed class DenyEverythingPolicy : IToolPolicyService
    {
        public PolicyDecision Evaluate(INetworkTool tool, ToolExecutionContext context) => PolicyDecision.Deny;
    }

    private sealed class RequireApprovalPolicy : IToolPolicyService
    {
        public PolicyDecision Evaluate(INetworkTool tool, ToolExecutionContext context) => PolicyDecision.RequireApproval;
    }
}
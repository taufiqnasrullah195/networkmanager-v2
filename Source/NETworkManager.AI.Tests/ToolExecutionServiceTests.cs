using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolExecutionServiceTests
{
    private static ToolExecutionService CreateService(params INetworkTool[] tools)
    {
        var registry = new ToolRegistry();
        foreach (var tool in tools)
            registry.Register(tool);

        return new ToolExecutionService(registry);
    }

    private sealed record FakeInput(string? Value) : IValidatableToolInput
    {
        public IReadOnlyList<string> Validate() =>
            string.IsNullOrWhiteSpace(Value) ? new[] { "Value is required." } : Array.Empty<string>();
    }

    [Fact]
    public async Task UnknownTool_returns_ToolNotFound()
    {
        var service = CreateService();

        var result = await service.ExecuteAsync("missing", null, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("ToolNotFound", result.ErrorCode);
        Assert.Equal("missing", result.ToolName);
    }

    [Fact]
    public async Task InvalidInputType_returns_InvalidInputType()
    {
        var service = CreateService(new FakeNetworkTool { Name = "ping", InputType = typeof(PingInput) });

        var result = await service.ExecuteAsync("ping", new object(), new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidInputType", result.ErrorCode);
    }

    [Fact]
    public async Task InvalidInput_returns_ValidationFailed()
    {
        var service = CreateService(new FakeNetworkTool { Name = "probe", InputType = typeof(FakeInput) });

        var result = await service.ExecuteAsync("probe", new FakeInput(" "), new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("ValidationFailed", result.ErrorCode);
        Assert.Contains("Value is required", result.Error);
    }

    [Fact]
    public async Task RequiresApproval_without_grant_returns_ApprovalRequired()
    {
        var service = CreateService(new FakeNetworkTool
        {
            Name = "restart",
            RequiresApproval = true,
            RiskLevel = ToolRiskLevel.Medium,
        });

        var result = await service.ExecuteAsync("restart", null, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("ApprovalRequired", result.ErrorCode);
        Assert.Equal(ToolRiskLevel.Medium, result.RiskLevel);
    }

    [Fact]
    public async Task RequiresApproval_with_grant_executes()
    {
        var service = CreateService(new FakeNetworkTool
        {
            Name = "restart",
            RequiresApproval = true,
            Handler = (_, _, _) => Task.FromResult(ToolOutcome.Ok("done")),
        });

        var result = await service.ExecuteAsync("restart", null,
            new ToolExecutionContext { ApprovalGranted = true });

        Assert.True(result.Success);
        Assert.Equal("done", result.Data);
    }

    [Fact]
    public async Task SuccessfulExecution_returns_data_and_metadata()
    {
        var started = DateTimeOffset.UtcNow;
        var service = CreateService(new FakeNetworkTool
        {
            Name = "ping",
            RiskLevel = ToolRiskLevel.Low,
            Handler = (_, _, _) => Task.FromResult(ToolOutcome.Ok(42)),
        });

        var result = await service.ExecuteAsync("ping", null, new ToolExecutionContext());

        Assert.True(result.Success);
        Assert.Equal("ping", result.ToolName);
        Assert.Equal(ToolRiskLevel.Low, result.RiskLevel);
        Assert.Equal(42, result.Data);
        Assert.Null(result.Error);
        Assert.True(result.Timestamp >= started);
        Assert.True(result.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task FailedOutcome_is_propagated()
    {
        var service = CreateService(new FakeNetworkTool
        {
            Name = "ping",
            Handler = (_, _, _) => Task.FromResult(ToolOutcome.Failed("SomeCode", "boom")),
        });

        var result = await service.ExecuteAsync("ping", null, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("SomeCode", result.ErrorCode);
        Assert.Equal("boom", result.Error);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task HangingTool_respects_Timeout()
    {
        var service = CreateService(new FakeNetworkTool
        {
            Name = "slow",
            Timeout = TimeSpan.FromMilliseconds(100),
            Handler = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                return ToolOutcome.Ok();
            },
        });

        var result = await service.ExecuteAsync("slow", null, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("TimedOut", result.ErrorCode);
    }

    [Fact]
    public async Task CancelledToken_returns_Cancelled()
    {
        var service = CreateService(new FakeNetworkTool
        {
            Name = "slow",
            Timeout = TimeSpan.FromSeconds(30),
            Handler = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
                return ToolOutcome.Ok();
            },
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.ExecuteAsync("slow", null, new ToolExecutionContext(), cts.Token);

        Assert.False(result.Success);
        Assert.Equal("Cancelled", result.ErrorCode);
    }

    [Fact]
    public async Task UnexpectedException_returns_ExecutionError()
    {
        var service = CreateService(new FakeNetworkTool
        {
            Name = "throws",
            Handler = (_, _, _) => throw new InvalidOperationException("kaboom"),
        });

        var result = await service.ExecuteAsync("throws", null, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("ExecutionError", result.ErrorCode);
        Assert.Contains("kaboom", result.Error);
    }
}
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolCallExecutorTests
{
    [Fact]
    public async Task Valid_tool_call_deserializes_arguments_and_executes()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool
        {
            Name = "ping",
            InputType = typeof(PingInput),
            Handler = (input, _, _) => Task.FromResult(ToolOutcome.Ok(input)),
        });

        var service = new ToolExecutionService(registry);

        var result = await service.ExecuteAsync(new AIToolCall
        {
            CallId = "call-1",
            ToolName = "ping",
            ArgumentsJson = """{"target":"192.168.1.1","count":4}""",
        }, new ToolExecutionContext());

        Assert.True(result.Success);
        Assert.Equal("ping", result.ToolName);

        var ping = Assert.IsType<PingInput>(result.Data);
        Assert.Equal("192.168.1.1", ping.Target);
        Assert.Equal(4, ping.Count);
        Assert.Equal(4000, ping.TimeoutMilliseconds); // initializer default preserved when omitted
    }

    [Fact]
    public async Task Unknown_tool_is_rejected()
    {
        var service = new ToolExecutionService(new ToolRegistry());

        var result = await service.ExecuteAsync(new AIToolCall
        {
            CallId = "call-1",
            ToolName = "execute_powershell",
            ArgumentsJson = """{}""",
        }, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("ToolNotFound", result.ErrorCode);
    }

    [Fact]
    public async Task Malformed_arguments_json_is_rejected()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool { Name = "ping", InputType = typeof(PingInput) });

        var service = new ToolExecutionService(registry);

        var result = await service.ExecuteAsync(new AIToolCall
        {
            CallId = "call-1",
            ToolName = "ping",
            ArgumentsJson = """{not-json}""",
        }, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.ErrorCode);
    }

    [Fact]
    public async Task Invalid_arguments_are_rejected_by_validation()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool { Name = "ping", InputType = typeof(PingInput) });

        var service = new ToolExecutionService(registry);

        var result = await service.ExecuteAsync(new AIToolCall
        {
            CallId = "call-1",
            ToolName = "ping",
            ArgumentsJson = """{"target":"","count":4}""",
        }, new ToolExecutionContext());

        Assert.False(result.Success);
        Assert.Equal("ValidationFailed", result.ErrorCode);
    }
}
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolRegistryTests
{
    [Fact]
    public void Register_then_List_returns_tool()
    {
        var registry = new ToolRegistry();
        var tool = new FakeNetworkTool { Name = "ping" };

        registry.Register(tool);

        var list = registry.List();
        Assert.Single(list);
        Assert.Same(tool, list[0]);
    }

    [Fact]
    public void TryGet_is_case_insensitive()
    {
        var registry = new ToolRegistry();
        var tool = new FakeNetworkTool { Name = "Ping" };
        registry.Register(tool);

        var found = registry.TryGet("ping", out var resolved);

        Assert.True(found);
        Assert.Same(tool, resolved);
    }

    [Fact]
    public void TryGet_unknown_returns_false()
    {
        var registry = new ToolRegistry();

        var found = registry.TryGet("missing", out var resolved);

        Assert.False(found);
        Assert.Null(resolved);
    }

    [Fact]
    public void Contains_reflects_registration()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool { Name = "ping" });

        Assert.True(registry.Contains("ping"));
        Assert.False(registry.Contains("traceroute"));
    }

    [Fact]
    public void Register_second_tool_with_same_name_replaces()
    {
        var registry = new ToolRegistry();
        var first = new FakeNetworkTool { Name = "ping" };
        var second = new FakeNetworkTool { Name = "ping" };

        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.List().Single());
    }

    [Fact]
    public void Register_empty_name_throws()
    {
        var registry = new ToolRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(new FakeNetworkTool { Name = " " }));
    }
}
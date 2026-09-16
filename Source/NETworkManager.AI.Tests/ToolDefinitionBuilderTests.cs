using NETworkManager.AI.Models;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolDefinitionBuilderTests
{
    [Fact]
    public void Build_produces_name_description_risk_and_reflected_schema()
    {
        var tool = new FakeNetworkTool
        {
            Name = "ping",
            Description = "Test ICMP connectivity",
            RiskLevel = ToolRiskLevel.Low,
            InputType = typeof(PingInput),
        };

        var definition = ToolDefinitionBuilder.Build(tool);

        Assert.Equal("ping", definition.Name);
        Assert.Equal("Test ICMP connectivity", definition.Description);
        Assert.Equal("Low", definition.RiskLevel);
        Assert.Equal("string", definition.InputSchema["Target"]);
        Assert.Equal("integer", definition.InputSchema["Count"]);
        Assert.Equal("integer", definition.InputSchema["TimeoutMilliseconds"]);
    }

    [Fact]
    public void BuildAll_lists_registered_tools()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool { Name = "ping", InputType = typeof(PingInput) });
        registry.Register(new FakeNetworkTool { Name = "tcp_test", InputType = typeof(TcpTestInput) });

        var definitions = ToolDefinitionBuilder.BuildAll(registry);

        Assert.Equal(2, definitions.Count);
        Assert.Contains(definitions, d => d.Name == "ping");
        Assert.Contains(definitions, d => d.Name == "tcp_test");
    }
}
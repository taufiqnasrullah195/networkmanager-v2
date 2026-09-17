using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringAiBoundaryTests
{
    [Fact]
    public async Task Monitoring_status_tool_has_no_configuration_mutation_surface()
    {
        var executor = new ScriptedMonitoringExecutor();
        var engine = new MonitoringEngine(new MonitoringOptions(), executor);
        var tool = new MonitoringStatusTool(engine);

        // The AI-facing tool is read-only: it only implements INetworkTool + exposes query projections.
        // Assert none of its public members are configuration mutation operations.
        var methods = typeof(MonitoringStatusTool).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var mutationHints = new[] { "Profile", "Target", "Check", "Create", "Delete", "Enable", "Disable", "Update", "Save" };

        Assert.DoesNotContain(methods, m => mutationHints.Any(h => m.Name.Contains(h, StringComparison.OrdinalIgnoreCase)));

        // The tool is constructed solely from IMonitoringQuery, so it cannot reach the profile service.
        var ctor = typeof(MonitoringStatusTool).GetConstructors().Single();
        Assert.Equal(typeof(Abstractions.IMonitoringQuery), ctor.GetParameters().Single().ParameterType);

        // And executing it never mutates the engine.
        var before = engine.GetCurrentStatus().Count;
        await tool.ExecuteAsync(new MonitoringStatusInput(), new NETworkManager.AI.Models.ToolExecutionContext(), CancellationToken.None);
        Assert.Equal(before, engine.GetCurrentStatus().Count);
    }

    [Fact]
    public void Configuration_service_is_not_an_ai_tool()
    {
        // The profile service writes configuration; it must NOT implement INetworkTool (so it can never be registered
        // into a tool registry and reached by the AI orchestration path).
        var serviceType = typeof(MonitoringProfileService);
        Assert.False(typeof(NETworkManager.AI.Abstractions.INetworkTool).IsAssignableFrom(serviceType));
    }
}
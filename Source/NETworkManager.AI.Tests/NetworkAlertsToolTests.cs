using NETworkManager.AI.Alerts;
using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class NetworkAlertsToolTests
{
    private static AlertEngine EngineWithOneAlert()
    {
        var engine = new AlertEngine();
        engine.Start();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        return engine;
    }

    [Fact]
    public void Tool_metadata_is_low_risk_read_only()
    {
        var tool = new NetworkAlertsTool(EngineWithOneAlert());

        Assert.Equal("network_alerts", tool.Name);
        Assert.Equal(ToolCategory.Alerts, tool.Category);
        Assert.Equal(ToolRiskLevel.Low, tool.RiskLevel);
        Assert.False(tool.RequiresApproval);
    }

    [Fact]
    public async Task Returns_structured_alert_data()
    {
        var tool = new NetworkAlertsTool(EngineWithOneAlert());

        var outcome = await tool.ExecuteAsync(new NetworkAlertsInput(), new ToolExecutionContext(), CancellationToken.None);

        Assert.True(outcome.Success);
        var result = Assert.IsType<NetworkAlertsResult>(outcome.Data);
        var alert = Assert.Single(result.Alerts);
        Assert.Equal("gw", alert.TargetId);
        Assert.Equal(nameof(AlertSeverity.Error), alert.Severity);
        Assert.Equal(nameof(AlertStatus.Open), alert.Status);
        Assert.Equal(nameof(NetworkHealthStatus.Unhealthy), alert.CurrentHealthState);
        Assert.Equal(1, alert.OccurrenceCount);
    }

    [Fact]
    public async Task Supports_target_filter_and_include_resolved()
    {
        var engine = EngineWithOneAlert();
        var id = Assert.Single(engine.GetActiveAlerts()).AlertId;
        engine.ResolveAlert(id, "recovered");

        var tool = new NetworkAlertsTool(engine);

        // Active only (default): empty after resolve.
        var active = await tool.ExecuteAsync(new NetworkAlertsInput(), new ToolExecutionContext(), CancellationToken.None);
        Assert.Empty(Assert.IsType<NetworkAlertsResult>(active.Data).Alerts);

        // Include resolved: the resolved alert appears.
        var all = await tool.ExecuteAsync(new NetworkAlertsInput { IncludeResolved = true }, new ToolExecutionContext(), CancellationToken.None);
        Assert.Single(Assert.IsType<NetworkAlertsResult>(all.Data).Alerts);

        // Target filter: wrong target → empty.
        var filtered = await tool.ExecuteAsync(new NetworkAlertsInput { TargetId = "other", IncludeResolved = true }, new ToolExecutionContext(), CancellationToken.None);
        Assert.Empty(Assert.IsType<NetworkAlertsResult>(filtered.Data).Alerts);
    }

    [Fact]
    public async Task Tool_is_read_only_and_cannot_mutate()
    {
        var engine = EngineWithOneAlert();
        var tool = new NetworkAlertsTool(engine);

        var ctor = typeof(NetworkAlertsTool).GetConstructors().Single();
        Assert.Equal(typeof(Abstractions.IAlertQuery), ctor.GetParameters().Single().ParameterType);

        var methods = typeof(NetworkAlertsTool).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        Assert.DoesNotContain(methods, m => m.Name.Contains("Acknowledge", StringComparison.OrdinalIgnoreCase)
                                            || m.Name.Contains("Resolve", StringComparison.OrdinalIgnoreCase)
                                            || m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));

        var before = engine.GetActiveAlerts().Count;
        await tool.ExecuteAsync(new NetworkAlertsInput(), new ToolExecutionContext(), CancellationToken.None);
        Assert.Equal(before, engine.GetActiveAlerts().Count);
    }
}
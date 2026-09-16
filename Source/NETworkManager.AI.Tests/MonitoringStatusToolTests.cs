using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringStatusToolTests
{
    private static MonitoringResult Result(string targetId, MonitorCheckType type, MonitoringResultStatus status, string message) => new()
    {
        CheckId = $"{targetId}-{type}",
        TargetId = targetId,
        CheckType = type,
        Status = status,
        ErrorClassification = status == MonitoringResultStatus.Unhealthy ? MonitorErrorClass.Unreachable : MonitorErrorClass.None,
        Timestamp = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        SafeMessage = message,
        Observed = type == MonitorCheckType.Ping && status == MonitoringResultStatus.Healthy
            ? new PingResult { Target = "x", Success = true, Sent = 4, Received = 4, AverageLatencyMilliseconds = 3 }
            : null,
        CorrelationId = $"{targetId}-{type}",
    };

    private static (MonitoringEngine Engine, ScriptedMonitoringExecutor Executor) Boiled()
    {
        var executor = new ScriptedMonitoringExecutor();
        var engine = new MonitoringEngine(new MonitoringOptions(), executor);

        engine.AddTarget(new MonitoringTarget { Id = "gw", Name = "Gateway", IPAddress = "192.168.1.1" });
        engine.AddTarget(new MonitoringTarget { Id = "srv", Name = "File Server", IPAddress = "10.0.0.5" });

        engine.AddCheck(new MonitoringCheck { CheckId = "gw-ping", Type = MonitorCheckType.Ping, TargetId = "gw", Timeout = TimeSpan.FromSeconds(5) });
        engine.AddCheck(new MonitoringCheck { CheckId = "srv-ping", Type = MonitorCheckType.Ping, TargetId = "srv", Timeout = TimeSpan.FromSeconds(5) });

        executor.ResultFactory = (check, target) =>
            Result(target.Id, check.Type,
                target.Id == "gw" ? MonitoringResultStatus.Healthy : MonitoringResultStatus.Unhealthy,
                target.Id == "gw" ? "ok" : "unreachable");

        return (engine, executor);
    }

    [Fact]
    public void Tool_metadata_is_low_risk_read_only()
    {
        var (engine, _) = Boiled();
        var tool = new MonitoringStatusTool(engine);

        Assert.Equal("network_monitoring_status", tool.Name);
        Assert.Equal(ToolCategory.Monitoring, tool.Category);
        Assert.Equal(ToolRiskLevel.Low, tool.RiskLevel);
        Assert.False(tool.RequiresApproval);
        Assert.Equal(typeof(MonitoringStatusInput), tool.InputType);
        Assert.Equal(typeof(MonitoringStatusResult), tool.OutputType);
    }

    [Fact]
    public async Task Returns_structured_evidence_for_all_targets()
    {
        var (engine, _) = Boiled();
        await engine.RunCheckAsync(new MonitoringCheck { CheckId = "gw-ping", Type = MonitorCheckType.Ping, TargetId = "gw", Timeout = TimeSpan.FromSeconds(5) });
        await engine.RunCheckAsync(new MonitoringCheck { CheckId = "srv-ping", Type = MonitorCheckType.Ping, TargetId = "srv", Timeout = TimeSpan.FromSeconds(5) });

        var tool = new MonitoringStatusTool(engine);
        var outcome = await tool.ExecuteAsync(new MonitoringStatusInput(), new ToolExecutionContext(), CancellationToken.None);

        Assert.True(outcome.Success);
        var result = Assert.IsType<MonitoringStatusResult>(outcome.Data);

        Assert.Equal(2, result.Targets.Count);
        Assert.Contains(result.Targets, t => t.TargetId == "gw" && t.Health == nameof(NetworkHealthStatus.Healthy));
        Assert.Contains(result.Targets, t => t.TargetId == "srv" && t.Health == nameof(NetworkHealthStatus.Unhealthy));

        var failure = Assert.Single(result.RecentFailures);
        Assert.Equal("srv", failure.TargetId);
        Assert.Equal("unreachable", failure.SafeMessage);
    }

    [Fact]
    public async Task Supports_target_filter()
    {
        var (engine, _) = Boiled();
        await engine.RunCheckAsync(new MonitoringCheck { CheckId = "gw-ping", Type = MonitorCheckType.Ping, TargetId = "gw", Timeout = TimeSpan.FromSeconds(5) });
        await engine.RunCheckAsync(new MonitoringCheck { CheckId = "srv-ping", Type = MonitorCheckType.Ping, TargetId = "srv", Timeout = TimeSpan.FromSeconds(5) });

        var tool = new MonitoringStatusTool(engine);
        var outcome = await tool.ExecuteAsync(new MonitoringStatusInput { TargetId = "gw" }, new ToolExecutionContext(), CancellationToken.None);

        var result = Assert.IsType<MonitoringStatusResult>(outcome.Data);
        var target = Assert.Single(result.Targets);
        Assert.Equal("gw", target.TargetId);
    }

    [Fact]
    public async Task Cannot_modify_targets()
    {
        var (engine, _) = Boiled();
        await engine.RunCheckAsync(new MonitoringCheck { CheckId = "gw-ping", Type = MonitorCheckType.Ping, TargetId = "gw", Timeout = TimeSpan.FromSeconds(5) });

        var before = engine.GetCurrentStatus();
        var tool = new MonitoringStatusTool(engine);

        await tool.ExecuteAsync(new MonitoringStatusInput(), new ToolExecutionContext(), CancellationToken.None);

        var after = engine.GetCurrentStatus();
        Assert.Equal(before.Count, after.Count);

        // The tool depends only on IMonitoringQuery, so it has no mutation path — the snapshots are unchanged.
        Assert.Equal(
            before.Select(s => (s.TargetId, s.Health)),
            after.Select(s => (s.TargetId, s.Health)));
    }
}
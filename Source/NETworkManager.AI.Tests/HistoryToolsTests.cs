using NETworkManager.AI.Alerts;
using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Persistence;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class HistoryToolsTests
{
    [Fact]
    public async Task Tools_are_low_risk_read_only()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var monitoring = new MonitoringHistoryTool(new SqliteHistoryRepository(db.Database));
        var alerts = new AlertHistoryTool(new SqliteHistoryRepository(db.Database));

        Assert.Equal(ToolRiskLevel.Low, monitoring.RiskLevel);
        Assert.False(monitoring.RequiresApproval);
        Assert.Equal("network_monitoring_history", monitoring.Name);

        Assert.Equal(ToolRiskLevel.Low, alerts.RiskLevel);
        Assert.False(alerts.RequiresApproval);
        Assert.Equal("network_alert_history", alerts.Name);
    }

    [Fact]
    public async Task Monitoring_history_tool_returns_structured_evidence()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteMonitoringStateStore(db.Database);
        store.Record(new MonitoringResult
        {
            CheckId = "gw-ping", TargetId = "gw", CheckType = MonitorCheckType.Ping,
            Status = MonitoringResultStatus.Timeout, ErrorClassification = MonitorErrorClass.Timeout,
            Timestamp = DateTimeOffset.UtcNow, Duration = TimeSpan.FromMilliseconds(12),
            SafeMessage = "timed out",
        });

        var tool = new MonitoringHistoryTool(new SqliteHistoryRepository(db.Database));
        var outcome = await tool.ExecuteAsync(new MonitoringHistoryInput(), new ToolExecutionContext(), CancellationToken.None);

        Assert.True(outcome.Success);
        var result = Assert.IsType<MonitoringHistoryResult>(outcome.Data);
        var point = Assert.Single(result.Results);
        Assert.Equal("gw", point.TargetId);
        Assert.Equal("Timeout", point.Status);
        Assert.Equal(12, point.DurationMs);
    }

    [Fact]
    public async Task Alert_history_tool_returns_structured_evidence()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var store = new SqliteAlertStore(db.Database);
        store.CreateAlert(new Alert
        {
            AlertId = "a1", Fingerprint = "gw\u0001Ping", TargetId = "gw", TargetName = "Gateway",
            Severity = AlertSeverity.Error, Status = AlertStatus.Open, Title = "down",
            FirstSeenAt = DateTimeOffset.UtcNow, LastSeenAt = DateTimeOffset.UtcNow, OccurrenceCount = 3,
            PreviousHealthState = NetworkHealthStatus.Healthy, CurrentHealthState = NetworkHealthStatus.Unhealthy,
        });

        var tool = new AlertHistoryTool(new SqliteHistoryRepository(db.Database));
        var outcome = await tool.ExecuteAsync(new AlertHistoryInput(), new ToolExecutionContext(), CancellationToken.None);

        Assert.True(outcome.Success);
        var result = Assert.IsType<AlertHistoryResult>(outcome.Data);
        var point = Assert.Single(result.Alerts);
        Assert.Equal("gw", point.TargetId);
        Assert.Equal("Error", point.Severity);
        Assert.Equal(3, point.OccurrenceCount);
    }

    [Fact]
    public async Task Tools_have_no_mutation_surface()
    {
        using var db = new SqliteTestDb();
        await db.InitializeAsync();

        var repo = new SqliteHistoryRepository(db.Database);
        var monitoringCtor = typeof(MonitoringHistoryTool).GetConstructors().Single();
        var alertCtor = typeof(AlertHistoryTool).GetConstructors().Single();

        Assert.Equal(typeof(Abstractions.IMonitoringHistoryRepository), monitoringCtor.GetParameters().Single().ParameterType);
        Assert.Equal(typeof(Abstractions.IAlertHistoryRepository), alertCtor.GetParameters().Single().ParameterType);

        var methods = typeof(MonitoringHistoryTool).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Concat(typeof(AlertHistoryTool).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));

        Assert.DoesNotContain(methods, m => m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                                            || m.Name.Contains("Resolve", StringComparison.OrdinalIgnoreCase)
                                            || m.Name.Contains("Acknowledge", StringComparison.OrdinalIgnoreCase)
                                            || m.Name.Contains("Create", StringComparison.OrdinalIgnoreCase));
    }
}
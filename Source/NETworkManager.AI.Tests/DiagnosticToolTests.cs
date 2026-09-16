using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class DiagnosticToolTests
{
    private static ToolRegistry RegistryWithDiagnostic()
    {
        var registry = FakeNetwork.Registry();
        var execution = new ToolExecutionService(registry);
        var tool = new InternetConnectivityDiagnosticTool(new DiagnosticEngine(execution));
        registry.Register(tool);
        return registry;
    }

    [Fact]
    public void Diagnostic_tool_registers_as_low_risk_read_only()
    {
        var registry = RegistryWithDiagnostic();

        Assert.True(registry.TryGet("internet_connectivity_diagnostic", out var tool));
        Assert.Equal(ToolRiskLevel.Low, tool!.RiskLevel);
        Assert.False(tool.RequiresApproval);
        Assert.Equal(typeof(DiagnosticReport), tool.OutputType);
    }

    [Fact]
    public async Task Diagnostic_tool_is_invocable_via_tool_call_and_returns_report()
    {
        var registry = RegistryWithDiagnostic();
        var execution = new ToolExecutionService(registry);

        var result = await execution.ExecuteAsync(
            new AIToolCall { CallId = "d1", ToolName = "internet_connectivity_diagnostic", ArgumentsJson = "{}" },
            new ToolExecutionContext());

        Assert.True(result.Success);
        var report = Assert.IsType<DiagnosticReport>(result.Data);
        Assert.Equal("Internet Connectivity Diagnostic", report.Name);
        Assert.Equal(DiagnosticStatus.Success, report.Status);
        Assert.Equal(8, report.Evidence.Count);
    }

    [Fact]
    public async Task Diagnostic_tool_accepts_optional_target_arguments()
    {
        var registry = RegistryWithDiagnostic();
        var execution = new ToolExecutionService(registry);

        var result = await execution.ExecuteAsync(
            new AIToolCall { CallId = "d2", ToolName = "internet_connectivity_diagnostic", ArgumentsJson = """{"hostname":"example.com","port":443}""" },
            new ToolExecutionContext());

        Assert.True(result.Success);
        var report = Assert.IsType<DiagnosticReport>(result.Data);
        Assert.Equal("example.com", report.Target.Hostname);
        Assert.Equal(443, report.Target.Port);
    }
}
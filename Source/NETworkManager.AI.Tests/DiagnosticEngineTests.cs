using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Diagnostics.Workflows;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Models;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class DiagnosticEngineTests
{
    private static async Task<DiagnosticReport> RunAsync(ToolRegistry registry, DiagnosticTarget? target = null, CancellationToken ct = default)
    {
        var engine = new DiagnosticEngine(new ToolExecutionService(registry));
        return await engine.RunAsync(InternetConnectivityDiagnostic.Create(), target ?? DiagnosticTarget.Empty, ct);
    }

    [Fact]
    public async Task Scenario_A_all_up_is_success()
    {
        var report = await RunAsync(FakeNetwork.Registry());

        Assert.Equal(DiagnosticStatus.Success, report.Status);
        Assert.Equal(8, report.Steps.Count);
        Assert.Equal(8, report.Evidence.Count);
        Assert.All(report.Steps, s => Assert.Equal(CheckStatus.Passed, s.Status));
        Assert.All(report.Evidence, e => Assert.True(e.Success));
    }

    [Fact]
    public async Task Scenario_B_no_adapter_fails_first_step_and_skips_the_rest()
    {
        var report = await RunAsync(FakeNetwork.Registry(adapterActive: false));

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal(CheckStatus.Failed, report.Steps.First(s => s.StepId == "adapter").Status);
        Assert.Equal(7, report.Steps.Count(s => s.Status == CheckStatus.Skipped));
    }

    [Fact]
    public async Task Skipped_steps_carry_a_reason_and_are_not_counted_as_failures()
    {
        var report = await RunAsync(FakeNetwork.Registry(adapterActive: false));

        var skipped = report.Steps.Where(s => s.Status == CheckStatus.Skipped).ToList();
        Assert.Equal(7, skipped.Count);
        Assert.All(skipped, s => Assert.Contains("Blocked by previous failure", s.SkipReason));
        Assert.Single(report.FailedChecks); // only 'adapter' failed
    }

    [Fact]
    public async Task Scenario_C_no_ip_gives_no_ip_address()
    {
        var report = await RunAsync(FakeNetwork.Registry(hasIp: false));

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal(CheckStatus.Failed, report.Steps.First(s => s.StepId == "ip_config").Status);
        Assert.Contains(report.Steps, s => s.Status == CheckStatus.Skipped && s.StepId == "default_route");
    }

    [Fact]
    public async Task Scenario_D_gateway_unreachable_skips_downstream_connectivity()
    {
        var report = await RunAsync(FakeNetwork.Registry(gatewayReachable: false));

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal(CheckStatus.Failed, report.Steps.First(s => s.StepId == "gateway_ping").Status);
        Assert.Contains(report.Steps, s => s.Status == CheckStatus.Skipped && s.StepId == "external_ping");
        Assert.Contains(report.Steps, s => s.Status == CheckStatus.Skipped && s.StepId == "tcp_test");
        Assert.Contains(report.Steps, s => s.Status == CheckStatus.Skipped && s.StepId == "dns_lookup");
    }

    [Fact]
    public async Task Scenario_E_dns_failure_is_detected()
    {
        var report = await RunAsync(FakeNetwork.Registry(dnsOk: false));

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal(CheckStatus.Failed, report.Steps.First(s => s.StepId == "dns_lookup").Status);
    }

    [Fact]
    public async Task Scenario_F_tcp_failure_is_detected()
    {
        var report = await RunAsync(FakeNetwork.Registry(tcpOk: false));

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal(CheckStatus.Failed, report.Steps.First(s => s.StepId == "tcp_test").Status);
    }

    [Fact]
    public async Task Multiple_failures_are_preserved_in_order()
    {
        var report = await RunAsync(FakeNetwork.Registry(dnsOk: false, tcpOk: false));

        Assert.Equal(2, report.FailedChecks.Count);
        Assert.Equal("dns_lookup", report.FailedChecks[0].StepId);
        Assert.Equal("tcp_test", report.FailedChecks[1].StepId);
    }

    [Fact]
    public async Task Evidence_is_preserved_in_execution_order()
    {
        var report = await RunAsync(FakeNetwork.Registry());

        Assert.Equal(
            new[] { "adapter", "ip_config", "default_route", "gateway_ping", "dns_lookup", "external_ping", "tcp_test", "traceroute" },
            report.Evidence.Select(e => e.StepId));

        var gatewayPing = report.Evidence.First(e => e.StepId == "gateway_ping");
        Assert.Equal("ping", gatewayPing.Tool);
        Assert.Equal("192.168.1.1", gatewayPing.Target);
        Assert.NotEqual(default, gatewayPing.Timestamp);

        var ping = Assert.IsType<PingResult>(gatewayPing.Data);
        Assert.True(ping.Success);
        Assert.Equal(4, ping.Received);
    }

    [Fact]
    public async Task Tool_failure_does_not_crash_the_engine()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool
        {
            Name = "network_adapter_info",
            InputType = typeof(NetworkAdapterInput),
            Handler = (_, _, _) => Task.FromResult(ToolOutcome.Failed("AdapterInfoFailed", "boom")),
        });
        registry.Register(FakeNetwork.RoutingTool(true));
        registry.Register(FakeNetwork.PingTool(true));
        registry.Register(FakeNetwork.DnsTool(true));
        registry.Register(FakeNetwork.TcpTool(true));
        registry.Register(FakeNetwork.TracerouteTool(true));

        var report = await RunAsync(registry);

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal("AdapterInfoFailed", report.Steps.First(s => s.StepId == "adapter").Evidence!.ErrorCode);
    }

    [Fact]
    public async Task Unknown_tool_step_fails_gracefully()
    {
        var workflow = new DiagnosticWorkflow
        {
            Name = "unknown",
            Severity = "LOW",
            Steps = new[]
            {
                new DiagnosticStep { Id = "s1", Name = "s1", ToolName = "does_not_exist", ArgumentsJson = "{}", ClassifiesAs = FailureClass.Unknown },
            },
        };

        var engine = new DiagnosticEngine(new ToolExecutionService(new ToolRegistry()));
        var report = await engine.RunAsync(workflow, DiagnosticTarget.Empty);

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal("ToolNotFound", report.Steps[0].Evidence!.ErrorCode);
    }

    [Fact]
    public async Task Cancellation_marks_all_steps_cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var report = await RunAsync(FakeNetwork.Registry(), ct: cts.Token);

        Assert.Equal(DiagnosticStatus.Cancelled, report.Status);
        Assert.All(report.Steps, s => Assert.Equal(CheckStatus.Cancelled, s.Status));
    }

    [Fact]
    public async Task Step_timeout_is_recorded_as_timed_out()
    {
        var registry = new ToolRegistry();
        registry.Register(new FakeNetworkTool
        {
            Name = "slow",
            InputType = typeof(NetworkAdapterInput),
            Timeout = TimeSpan.FromSeconds(30),
            Handler = async (_, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
                return ToolOutcome.Ok();
            },
        });

        var workflow = new DiagnosticWorkflow
        {
            Name = "slow-workflow",
            Severity = "LOW",
            Steps = new[]
            {
                new DiagnosticStep { Id = "s1", Name = "s1", ToolName = "slow", ArgumentsJson = "{}", Timeout = TimeSpan.FromMilliseconds(200), ClassifiesAs = FailureClass.Unknown },
            },
        };

        var engine = new DiagnosticEngine(new ToolExecutionService(registry));
        var report = await engine.RunAsync(workflow, DiagnosticTarget.Empty);

        Assert.Equal(DiagnosticStatus.Failed, report.Status);
        Assert.Equal("TimedOut", report.Steps[0].Evidence!.ErrorCode);
    }
}
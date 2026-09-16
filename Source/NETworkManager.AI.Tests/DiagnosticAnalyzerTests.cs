using NETworkManager.AI.Diagnostics;
using NETworkManager.AI.Diagnostics.Workflows;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class DiagnosticAnalyzerTests
{
    private static async Task<DiagnosticAnalysis> AnalyzeAsync(ToolRegistry registry)
    {
        var engine = new DiagnosticEngine(new ToolExecutionService(registry));
        var report = await engine.RunAsync(InternetConnectivityDiagnostic.Create(), DiagnosticTarget.Empty);
        return new DefaultDiagnosticAnalyzer().Analyze(report);
    }

    [Fact]
    public async Task All_up_classifies_none()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry());

        Assert.Equal(FailureClass.None, analysis.Classification);
        Assert.Equal(DiagnosticStatus.Success, analysis.Status);
    }

    [Fact]
    public async Task No_adapter_classifies_no_network_adapter()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry(adapterActive: false));

        Assert.Equal(FailureClass.NoNetworkAdapter, analysis.Classification);
    }

    [Fact]
    public async Task No_ip_classifies_no_ip_address()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry(hasIp: false));

        Assert.Equal(FailureClass.NoIpAddress, analysis.Classification);
    }

    [Fact]
    public async Task Gateway_unreachable_classifies_gateway_unreachable()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry(gatewayReachable: false));

        Assert.Equal(FailureClass.GatewayUnreachable, analysis.Classification);
    }

    [Fact]
    public async Task Dns_failure_classifies_dns_failure()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry(dnsOk: false));

        Assert.Equal(FailureClass.DnsFailure, analysis.Classification);
    }

    [Fact]
    public async Task Tcp_failure_classifies_tcp_connectivity_failure()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry(tcpOk: false));

        Assert.Equal(FailureClass.TcpConnectivityFailure, analysis.Classification);
    }

    [Fact]
    public async Task Analyzer_reports_symptom_not_unproven_root_cause()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry(dnsOk: false));

        Assert.Equal("Internet Connectivity Diagnostic: DNS resolution failed.", analysis.Summary);
        Assert.DoesNotContain("broken", analysis.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Check the configured DNS servers.", analysis.Recommendation);
    }

    [Fact]
    public async Task Analyzer_produces_findings_and_recommendation()
    {
        var analysis = await AnalyzeAsync(FakeNetwork.Registry(dnsOk: false));

        Assert.Contains(analysis.Findings, f => f.Contains("✓ gateway_ping", StringComparison.Ordinal));
        Assert.Contains(analysis.Findings, f => f.Contains("✗ dns_lookup", StringComparison.Ordinal));
        Assert.NotNull(analysis.Recommendation);
    }
}
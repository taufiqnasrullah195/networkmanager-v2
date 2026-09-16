using NETworkManager.AI.Models;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringCheckExecutorTests
{
    private static MonitoringTarget Target(string id = "t1") => new() { Id = id, IPAddress = "192.168.1.1" };

    private static MonitoringCheck Check(MonitorCheckType type, int port = 443, TimeSpan? timeout = null) => new()
    {
        CheckId = "c1",
        Type = type,
        TargetId = "t1",
        Port = port,
        Timeout = timeout ?? TimeSpan.FromSeconds(5),
    };

    [Fact]
    public async Task Ping_healthy_yields_healthy_result_with_evidence()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(
            MonitoringNetwork.Ping(() => new PingResult
            {
                Target = "192.168.1.1",
                Success = true,
                Sent = 4,
                Received = 4,
                AverageLatencyMilliseconds = 4,
            })));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.Ping), Target());

        Assert.Equal(MonitoringResultStatus.Healthy, result.Status);
        Assert.Equal(MonitorErrorClass.None, result.ErrorClassification);
        Assert.NotNull(result.Evidence);
        Assert.Equal("NetworkTool.ping", result.Evidence!.Source);
        Assert.IsType<PingResult>(result.Observed);
        Assert.Equal("c1", result.CorrelationId);
    }

    [Fact]
    public async Task Ping_unreachable_yields_unhealthy_with_cautious_message()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(
            MonitoringNetwork.Ping(() => new PingResult { Target = "192.168.1.1", Success = false, Sent = 4, Received = 0 })));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.Ping), Target());

        Assert.Equal(MonitoringResultStatus.Unhealthy, result.Status);
        Assert.Equal(MonitorErrorClass.Unreachable, result.ErrorClassification);
        Assert.Contains("could not be confirmed via ICMP", result.SafeMessage);
    }

    [Fact]
    public async Task Ping_timeout_is_distinct_from_unreachable()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(
            MonitoringNetwork.Ping(() => new PingResult { Target = "192.168.1.1", Success = false, Sent = 4, Received = 0, TimedOutCount = 4 })));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.Ping), Target());

        Assert.Equal(MonitoringResultStatus.Timeout, result.Status);
        Assert.Equal(MonitorErrorClass.Timeout, result.ErrorClassification);
    }

    [Fact]
    public async Task Ping_partial_loss_yields_warning()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(
            MonitoringNetwork.Ping(() => new PingResult { Target = "192.168.1.1", Success = true, Sent = 4, Received = 2, PacketLossPercent = 50 })));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.Ping), Target());

        Assert.Equal(MonitoringResultStatus.Warning, result.Status);
    }

    [Fact]
    public async Task Ping_name_resolution_failure_yields_unhealthy_name_resolution()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(
            MonitoringNetwork.PingFailure("ResolutionFailed", "Could not resolve host.")));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.Ping), Target());

        Assert.Equal(MonitoringResultStatus.Unhealthy, result.Status);
        Assert.Equal(MonitorErrorClass.NameResolution, result.ErrorClassification);
    }

    [Theory]
    [InlineData("Open", MonitoringResultStatus.Healthy)]
    [InlineData("Closed", MonitoringResultStatus.Unhealthy)]
    [InlineData("TimedOut", MonitoringResultStatus.Timeout)]
    public async Task Tcp_state_maps_correctly(string state, MonitoringResultStatus expected)
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(MonitoringNetwork.Tcp(state)));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.TcpConnectivity, port: 443), Target());

        Assert.Equal(expected, result.Status);

        if (state == "Closed")
            Assert.Equal(MonitorErrorClass.TcpConnectivity, result.ErrorClassification);
        else if (state == "TimedOut")
            Assert.Equal(MonitorErrorClass.Timeout, result.ErrorClassification);
    }

    [Fact]
    public async Task Dns_success_yields_healthy_with_resolved_address()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(MonitoringNetwork.Dns(true)));

        var target = new MonitoringTarget { Id = "t1", Hostname = "server01" };
        var result = await executor.ExecuteAsync(Check(MonitorCheckType.DnsResolution), target);

        Assert.Equal(MonitoringResultStatus.Healthy, result.Status);
        Assert.Contains("10.0.0.20", result.SafeMessage);
    }

    [Fact]
    public async Task Dns_failure_yields_unhealthy_dns_resolution_not_server_down()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(MonitoringNetwork.Dns(false)));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.DnsResolution), new MonitoringTarget { Id = "t1", Hostname = "server01" });

        Assert.Equal(MonitoringResultStatus.Unhealthy, result.Status);
        Assert.Equal(MonitorErrorClass.DnsResolution, result.ErrorClassification);
        Assert.DoesNotContain("server is down", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelled_token_yields_cancelled_result()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(
            MonitoringNetwork.Ping(() => new PingResult { Target = "192.168.1.1", Success = true, Sent = 4, Received = 4 })));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.Ping), Target(), cts.Token);

        Assert.Equal(MonitoringResultStatus.Cancelled, result.Status);
        Assert.Equal(MonitorErrorClass.Cancelled, result.ErrorClassification);
    }

    [Fact]
    public async Task Target_without_address_yields_error()
    {
        var executor = new MonitoringCheckExecutor(MonitoringNetwork.Service(
            MonitoringNetwork.Ping(() => new PingResult { Target = "x", Success = true, Sent = 1, Received = 1 })));

        var result = await executor.ExecuteAsync(Check(MonitorCheckType.Ping), new MonitoringTarget { Id = "t1" });

        Assert.Equal(MonitoringResultStatus.Error, result.Status);
    }
}
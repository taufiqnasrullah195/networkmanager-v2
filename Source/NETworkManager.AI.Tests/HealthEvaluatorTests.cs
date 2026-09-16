using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Monitoring;
using Xunit;

namespace NETworkManager.AI.Tests;

public class HealthEvaluatorTests
{
    private readonly HealthEvaluator _evaluator = new();

    private static MonitoringResult With(MonitoringResultStatus status) => new()
    {
        CheckId = "c1",
        TargetId = "t1",
        CheckType = MonitorCheckType.Ping,
        Status = status,
        Timestamp = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        SafeMessage = "msg",
    };

    [Theory]
    [InlineData(MonitoringResultStatus.Healthy, NetworkHealthStatus.Healthy)]
    [InlineData(MonitoringResultStatus.Warning, NetworkHealthStatus.Degraded)]
    [InlineData(MonitoringResultStatus.Timeout, NetworkHealthStatus.Degraded)]
    [InlineData(MonitoringResultStatus.Unhealthy, NetworkHealthStatus.Unhealthy)]
    [InlineData(MonitoringResultStatus.Error, NetworkHealthStatus.Unhealthy)]
    [InlineData(MonitoringResultStatus.Cancelled, NetworkHealthStatus.Unknown)]
    [InlineData(MonitoringResultStatus.Unknown, NetworkHealthStatus.Unknown)]
    [InlineData(MonitoringResultStatus.Running, NetworkHealthStatus.Unknown)]
    public void Single_result_maps_to_expected_health(MonitoringResultStatus status, NetworkHealthStatus expected)
    {
        Assert.Equal(expected, _evaluator.Evaluate(With(status)));
    }

    [Fact]
    public void Empty_results_are_unknown()
    {
        Assert.Equal(NetworkHealthStatus.Unknown, _evaluator.Evaluate(Array.Empty<MonitoringResult>()));
    }

    [Fact]
    public void All_healthy_is_healthy()
    {
        Assert.Equal(NetworkHealthStatus.Healthy,
            _evaluator.Evaluate(new[] { With(MonitoringResultStatus.Healthy), With(MonitoringResultStatus.Healthy) }));
    }

    [Fact]
    public void Any_unhealthy_dominates()
    {
        Assert.Equal(NetworkHealthStatus.Unhealthy,
            _evaluator.Evaluate(new[] { With(MonitoringResultStatus.Healthy), With(MonitoringResultStatus.Unhealthy) }));
    }

    [Fact]
    public void Timeout_with_healthy_is_degraded_not_unhealthy()
    {
        Assert.Equal(NetworkHealthStatus.Degraded,
            _evaluator.Evaluate(new[] { With(MonitoringResultStatus.Healthy), With(MonitoringResultStatus.Timeout) }));
    }

    [Fact]
    public void Cancelled_only_is_unknown()
    {
        Assert.Equal(NetworkHealthStatus.Unknown,
            _evaluator.Evaluate(new[] { With(MonitoringResultStatus.Cancelled) }));
    }
}
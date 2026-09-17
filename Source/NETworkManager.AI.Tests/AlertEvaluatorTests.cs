using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class AlertEvaluatorTests
{
    private readonly AlertEvaluator _evaluator = new();

    [Fact]
    public void Healthy_to_unhealthy_is_error()
    {
        var d = _evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy));
        Assert.Equal(AlertDecisionKind.Create, d.Kind);
        Assert.Equal(AlertSeverity.Error, d.Severity);
    }

    [Fact]
    public void Healthy_to_degraded_is_warning()
    {
        var d = _evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Degraded));
        Assert.Equal(AlertDecisionKind.Create, d.Kind);
        Assert.Equal(AlertSeverity.Warning, d.Severity);
    }

    [Fact]
    public void Recovery_transitions_resolve()
    {
        Assert.Equal(AlertDecisionKind.Resolve, _evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy)).Kind);
        Assert.Equal(AlertDecisionKind.Resolve, _evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Degraded, NetworkHealthStatus.Healthy)).Kind);
    }

    [Fact]
    public void Unknown_source_is_ignored()
    {
        Assert.Equal(AlertDecisionKind.Ignore, _evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Unknown, NetworkHealthStatus.Unhealthy)).Kind);
    }

    [Fact]
    public void Configuration_disables_alerting()
    {
        var evaluator = new AlertEvaluator(new AlertOptions { AlertingEnabled = false });
        Assert.Equal(AlertDecisionKind.Ignore, evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy)).Kind);
    }

    [Fact]
    public void Configuration_disables_specific_severities()
    {
        var evaluator = new AlertEvaluator(new AlertOptions { CreateAlertOnUnhealthy = false, CreateAlertOnDegraded = false });

        Assert.Equal(AlertDecisionKind.Ignore, evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy)).Kind);
        Assert.Equal(AlertDecisionKind.Ignore, evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Degraded)).Kind);
    }

    [Fact]
    public void Configuration_disables_auto_resolve()
    {
        var evaluator = new AlertEvaluator(new AlertOptions { AutoResolveOnRecovery = false });
        Assert.Equal(AlertDecisionKind.Ignore, evaluator.Evaluate(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy)).Kind);
    }
}
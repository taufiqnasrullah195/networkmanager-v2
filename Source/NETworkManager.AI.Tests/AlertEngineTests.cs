using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class AlertEngineTests
{
    private static AlertEngine Started(AlertOptions? options = null, CapturingAlertObserver? observer = null)
    {
        var engine = new AlertEngine(options);
        if (observer is not null)
            engine.Subscribe(observer);
        engine.Start();
        return engine;
    }

    // ----- creation ---------------------------------------------------------

    [Fact]
    public void Healthy_to_unhealthy_creates_error_alert()
    {
        var engine = Started();
        var change = AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy));

        engine.ProcessHealthStateChanged(change);

        var alert = Assert.Single(engine.GetActiveAlerts());
        Assert.Equal(AlertSeverity.Error, alert.Severity);
        Assert.Equal(AlertStatus.Open, alert.Status);
        Assert.Equal("gw", alert.TargetId);
        Assert.Equal(1, alert.OccurrenceCount);
        Assert.Equal(NetworkHealthStatus.Unhealthy, alert.CurrentHealthState);
    }

    [Fact]
    public void Healthy_to_degraded_creates_warning_alert()
    {
        var engine = Started();
        var change = AlertTestData.Change("srv", NetworkHealthStatus.Healthy, NetworkHealthStatus.Degraded);

        engine.ProcessHealthStateChanged(change);

        var alert = Assert.Single(engine.GetActiveAlerts());
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
    }

    [Fact]
    public void Degraded_to_unhealthy_escalates_existing_alert()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Degraded,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Healthy, received: 2, sent: 4)));
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Degraded, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        var alert = Assert.Single(engine.GetActiveAlerts());
        Assert.Equal(AlertSeverity.Error, alert.Severity);
        Assert.Equal(NetworkHealthStatus.Unhealthy, alert.CurrentHealthState);
    }

    [Fact]
    public void Unhealthy_to_degraded_downgrades_alert()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Degraded,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Warning)));

        var alert = Assert.Single(engine.GetActiveAlerts());
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
    }

    [Fact]
    public void Unknown_to_unhealthy_is_ignored()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Unknown, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        Assert.Empty(engine.GetActiveAlerts());
    }

    // ----- deduplication ----------------------------------------------------

    [Fact]
    public void Recurring_failure_updates_same_alert_not_new_ones()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        engine.ProcessRecurringFailure(AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy));
        engine.ProcessRecurringFailure(AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy));

        var alert = Assert.Single(engine.GetActiveAlerts());
        Assert.Equal(3, alert.OccurrenceCount); // 1 (transition) + 2 (recurring)
    }

    [Fact]
    public void Recurring_failure_without_active_alert_is_ignored()
    {
        var engine = Started();
        engine.ProcessRecurringFailure(AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy));
        Assert.Empty(engine.GetActiveAlerts());
    }

    [Fact]
    public void Different_check_types_are_distinct_alerts()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            new MonitoringResult
            {
                CheckId = "gw-tcp",
                TargetId = "gw",
                CheckType = MonitorCheckType.TcpConnectivity,
                Status = MonitoringResultStatus.Unhealthy,
                Timestamp = DateTimeOffset.UtcNow,
                Duration = TimeSpan.Zero,
                SafeMessage = "tcp closed",
            }));

        Assert.Equal(2, engine.GetActiveAlerts().Count);
    }

    // ----- recovery ---------------------------------------------------------

    [Fact]
    public void Unhealthy_to_healthy_resolves_with_evidence()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        var recovery = AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Healthy));
        engine.ProcessHealthStateChanged(recovery);

        Assert.Empty(engine.GetActiveAlerts());
        var alert = Assert.Single(engine.GetRecentAlerts(10));
        Assert.Equal(AlertStatus.Resolved, alert.Status);
        Assert.NotNull(alert.ResolvedAt);
        Assert.Equal("ICMP echo reply received.", alert.ResolutionEvidence);
    }

    [Fact]
    public void Degraded_to_healthy_resolves()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Degraded));
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Degraded, NetworkHealthStatus.Healthy));

        Assert.Empty(engine.GetActiveAlerts());
    }

    // ----- acknowledgement --------------------------------------------------

    [Fact]
    public void Acknowledge_does_not_resolve_and_recovery_resolves_acknowledged()
    {
        var engine = Started();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        var alertId = Assert.Single(engine.GetActiveAlerts()).AlertId;
        Assert.True(engine.AcknowledgeAlert(alertId));

        var acknowledged = engine.GetAlert(alertId)!;
        Assert.Equal(AlertStatus.Acknowledged, acknowledged.Status);
        Assert.Single(engine.GetActiveAlerts()); // still active

        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Healthy)));

        Assert.Empty(engine.GetActiveAlerts());
        Assert.Equal(AlertStatus.Resolved, engine.GetAlert(alertId)!.Status);
    }

    [Fact]
    public void Acknowledge_unknown_or_resolved_alert_returns_false()
    {
        var engine = Started();
        Assert.False(engine.AcknowledgeAlert("missing"));

        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        var id = Assert.Single(engine.GetActiveAlerts()).AlertId;
        Assert.True(engine.AcknowledgeAlert(id));
        Assert.False(engine.AcknowledgeAlert(id)); // already acknowledged → not Open
    }

    // ----- lifecycle --------------------------------------------------------

    [Fact]
    public void Engine_ignores_events_when_stopped()
    {
        var engine = new AlertEngine();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        Assert.Empty(engine.GetActiveAlerts()); // not started → ignored

        engine.Start();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        Assert.Single(engine.GetActiveAlerts());

        engine.Stop();
        engine.ProcessRecurringFailure(AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy));
        Assert.Equal(1, Assert.Single(engine.GetActiveAlerts()).OccurrenceCount); // unchanged while stopped
    }

    // ----- configuration ----------------------------------------------------

    [Fact]
    public void Configuration_flags_are_respected()
    {
        var engine = Started(new AlertOptions { CreateAlertOnUnhealthy = false });
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        Assert.Empty(engine.GetActiveAlerts());
    }

    [Fact]
    public void Auto_resolve_disabled_keeps_alert_active()
    {
        var engine = Started(new AlertOptions { AutoResolveOnRecovery = false });
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Healthy)));

        Assert.Single(engine.GetActiveAlerts());
    }

    // ----- concurrency ------------------------------------------------------

    [Fact]
    public void Concurrent_identical_transitions_do_not_duplicate_alerts()
    {
        var engine = Started();
        var change = AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy));

        Parallel.For(0, 25, _ => engine.ProcessHealthStateChanged(change));

        Assert.Single(engine.GetActiveAlerts());
    }

    // ----- flapping ---------------------------------------------------------

    [Fact]
    public void Flapping_records_transitions_without_losing_recovery()
    {
        var engine = Started();
        var observer = new CapturingAlertObserver();
        engine.Subscribe(observer);

        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Healthy)));
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        Assert.Single(engine.GetActiveAlerts()); // one active (latest)
        Assert.Equal(2, observer.Created.Count); // two distinct create events
        Assert.Single(observer.Resolved);
    }

    // ----- events / observer ------------------------------------------------

    [Fact]
    public void Observer_receives_created_and_resolved_events()
    {
        var observer = new CapturingAlertObserver();
        var engine = Started(observer: observer);

        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        Assert.Single(observer.Created);
        Assert.Equal(AlertEventType.AlertCreated, observer.Created[0].Type);

        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Healthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Healthy)));

        Assert.Single(observer.Resolved);
    }

    // ----- security ---------------------------------------------------------

    [Fact]
    public void Alerts_and_logs_never_contain_credentials()
    {
        var logger = new CapturingAlertLogger();
        var engine = new AlertEngine(logger: logger);
        engine.Start();
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Healthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        var alert = Assert.Single(engine.GetActiveAlerts());
        var text = $"{alert.Title} {alert.Description} {alert.Reason} {alert.Evidence} {alert.FailureClassification}";
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(logger.Lines, l => l.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logger.Lines, l => l.Contains("token", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Invalid_change_is_handled_without_crash()
    {
        var engine = Started();
        // Previous == New (no transition) → evaluator returns Ignore.
        engine.ProcessHealthStateChanged(AlertTestData.Change("gw", NetworkHealthStatus.Unhealthy, NetworkHealthStatus.Unhealthy,
            AlertTestData.PingResult("gw", MonitoringResultStatus.Unhealthy)));

        Assert.Empty(engine.GetActiveAlerts());
    }
}
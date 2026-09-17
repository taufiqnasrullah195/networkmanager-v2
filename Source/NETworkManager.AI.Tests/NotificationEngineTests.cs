using NETworkManager.AI.Alerts;
using NETworkManager.AI.Notifications;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class NotificationEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    private static (NotificationEngine Engine, CollectingNotificationChannel Channel, NotificationStore Store, FakeAlertQuery Alerts)
        Build(NotificationPolicyConfig? policy = null, NotificationEngineOptions? options = null)
    {
        var channel = new CollectingNotificationChannel();
        var store = new NotificationStore();
        var alerts = new FakeAlertQuery();
        var service = new NotificationService(
            new Dictionary<string, INotificationChannel> { [channel.Name] = channel }, store, delay: _ => Task.CompletedTask);

        var engine = new NotificationEngine(service, new NotificationPolicy(policy ?? new NotificationPolicyConfig()),
            store, alerts, options);

        return (engine, channel, store, alerts);
    }

    private static AlertEvent Created(Alert alert, DateTimeOffset timestamp) => new()
    {
        Type = AlertEventType.AlertCreated,
        Alert = alert,
        Timestamp = timestamp,
    };

    private static AlertEvent Resolved(Alert alert, DateTimeOffset timestamp) => new()
    {
        Type = AlertEventType.AlertResolved,
        Alert = alert,
        Timestamp = timestamp,
    };

    [Fact]
    public async Task Alert_created_sends_notification()
    {
        var (engine, channel, _, _) = Build();
        var alert = DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Error, T0);

        var notifications = await engine.ProcessAlertEventAsync(Created(alert, T0), T0);

        var notification = Assert.Single(notifications);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(NotificationEventType.AlertCreated, notification.EventType);
        Assert.Single(channel.Sent);
        Assert.Equal("gw", channel.Sent[0].Target);
    }

    [Fact]
    public async Task Alert_resolved_sends_recovery_notification()
    {
        var (engine, channel, _, _) = Build();
        var alert = DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Error, T0, AlertStatus.Resolved);

        await engine.ProcessAlertEventAsync(Resolved(alert, T0), T0);

        var sent = Assert.Single(channel.Sent);
        Assert.Equal(NotificationEventType.AlertRecovered, sent.EventType);
        Assert.Contains("recovered", sent.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Recovery_notification_can_be_disabled()
    {
        var (engine, channel, _, _) = Build(new NotificationPolicyConfig { NotifyOnRecovery = false });
        var alert = DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Error, T0, AlertStatus.Resolved);

        await engine.ProcessAlertEventAsync(Resolved(alert, T0), T0);

        Assert.Empty(channel.Sent);
    }

    [Fact]
    public async Task Duplicate_event_does_not_send_twice()
    {
        var (engine, channel, _, _) = Build();
        var alert = DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Error, T0);

        await engine.ProcessAlertEventAsync(Created(alert, T0), T0);
        await engine.ProcessAlertEventAsync(Created(alert, T0.AddSeconds(30)), T0.AddSeconds(30));

        Assert.Single(channel.Sent);
    }

    [Fact]
    public async Task Escalation_rule_triggers_after_duration()
    {
        var (engine, channel, _, alerts) = Build(options: new NotificationEngineOptions
        {
            EscalationRules = new[] { new AlertEscalationRule { Level = 1, AfterDuration = TimeSpan.FromMinutes(15), EscalatedSeverity = AlertSeverity.Error } },
        });
        alerts.Active = new[] { DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Warning, T0) };

        var before = await engine.EvaluateEscalationAsync(T0.AddMinutes(10));
        Assert.Empty(before);

        var after = await engine.EvaluateEscalationAsync(T0.AddMinutes(16));
        Assert.NotEmpty(after);

        var sent = Assert.Single(channel.Sent);
        Assert.Equal(NotificationEventType.AlertEscalated, sent.EventType);
        Assert.Equal("Error", sent.Severity);
    }

    [Fact]
    public async Task Cooldown_suppresses_escalation_within_window()
    {
        var (engine, channel, _, alerts) = Build(options: new NotificationEngineOptions
        {
            Cooldown = TimeSpan.FromMinutes(15),
            EscalationRules = new[] { new AlertEscalationRule { Level = 1, AfterDuration = TimeSpan.FromMinutes(1), EscalatedSeverity = AlertSeverity.Error } },
        });

        var alert = DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Error, T0);
        alerts.Active = new[] { alert };

        // Initial notification at T0.
        await engine.ProcessAlertEventAsync(Created(alert, T0), T0);
        Assert.Single(channel.Sent);

        // Escalation matches at T0+5min but is within cooldown → suppressed.
        await engine.EvaluateEscalationAsync(T0.AddMinutes(5));
        Assert.Single(channel.Sent);

        // After cooldown (T0+16min) → escalation sent.
        await engine.EvaluateEscalationAsync(T0.AddMinutes(16));
        Assert.Equal(2, channel.Sent.Count);
        Assert.Equal(NotificationEventType.AlertEscalated, channel.Sent[1].EventType);
    }

    [Fact]
    public async Task Escalation_is_idempotent_per_level()
    {
        var (engine, channel, _, alerts) = Build(options: new NotificationEngineOptions
        {
            EscalationRules = new[] { new AlertEscalationRule { Level = 1, AfterDuration = TimeSpan.FromMinutes(1), EscalatedSeverity = AlertSeverity.Error } },
        });
        alerts.Active = new[] { DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Warning, T0) };

        await engine.EvaluateEscalationAsync(T0.AddMinutes(5));
        await engine.EvaluateEscalationAsync(T0.AddMinutes(6));

        Assert.Single(channel.Sent);
    }

    [Fact]
    public async Task Below_minimum_severity_alert_is_not_notified()
    {
        var (engine, channel, _, _) = Build(new NotificationPolicyConfig { MinimumSeverity = AlertSeverity.Warning });
        var alert = DashboardTestData.ActiveAlert("a1", "gw", AlertSeverity.Info, T0);

        await engine.ProcessAlertEventAsync(Created(alert, T0), T0);

        Assert.Empty(channel.Sent);
    }

    [Fact]
    public void Invalid_engine_options_are_rejected()
    {
        var options = new NotificationEngineOptions
        {
            EscalationRules = new[] { new AlertEscalationRule { Level = 0, AfterDuration = TimeSpan.FromMinutes(1) } },
        };

        Assert.NotEmpty(options.Validate());
    }
}

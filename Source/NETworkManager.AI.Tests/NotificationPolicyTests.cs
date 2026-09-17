using NETworkManager.AI.Alerts;
using NETworkManager.AI.Notifications;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class NotificationPolicyTests
{
    private static Alert Alert(AlertSeverity severity = AlertSeverity.Error) =>
        DashboardTestData.ActiveAlert("a1", "gw", severity);

    [Fact]
    public void Minimum_severity_suppresses_below_threshold()
    {
        var policy = new NotificationPolicy(new NotificationPolicyConfig { MinimumSeverity = AlertSeverity.Warning });

        Assert.False(policy.Decide(Alert(AlertSeverity.Info), NotificationEventType.AlertCreated, 0).ShouldNotify);
        Assert.True(policy.Decide(Alert(AlertSeverity.Warning), NotificationEventType.AlertCreated, 0).ShouldNotify);
        Assert.True(policy.Decide(Alert(AlertSeverity.Error), NotificationEventType.AlertCreated, 0).ShouldNotify);
    }

    [Fact]
    public void Disabled_policy_suppresses_everything()
    {
        var policy = new NotificationPolicy(new NotificationPolicyConfig { Enabled = false });
        Assert.False(policy.Decide(Alert(AlertSeverity.Critical), NotificationEventType.AlertCreated, 0).ShouldNotify);
    }

    [Fact]
    public void Event_filters_are_respected()
    {
        var policy = new NotificationPolicy(new NotificationPolicyConfig { NotifyOnCreated = false, NotifyOnRecovery = false });

        Assert.False(policy.Decide(Alert(), NotificationEventType.AlertCreated, 0).ShouldNotify);
        Assert.False(policy.Decide(Alert(), NotificationEventType.AlertRecovered, 0).ShouldNotify);
        Assert.True(policy.Decide(Alert(), NotificationEventType.AlertEscalated, 1).ShouldNotify);
    }

    [Fact]
    public void Recovery_can_be_disabled_independently()
    {
        var policy = new NotificationPolicy(new NotificationPolicyConfig { NotifyOnRecovery = false });
        Assert.False(policy.Decide(Alert(), NotificationEventType.AlertRecovered, 0).ShouldNotify);
        Assert.True(policy.Decide(Alert(), NotificationEventType.AlertCreated, 0).ShouldNotify);
    }

    [Fact]
    public void Channel_routing_falls_back_to_defaults()
    {
        var policy = new NotificationPolicy(new NotificationPolicyConfig
        {
            DefaultChannels = new[] { "Desktop" },
            ChannelRouting = new Dictionary<string, IReadOnlyList<string>> { ["Critical"] = new[] { "Desktop", "Email" } },
        });

        var critical = policy.Decide(Alert(AlertSeverity.Critical), NotificationEventType.AlertCreated, 0);
        Assert.Equal(new[] { "Desktop", "Email" }, critical.Channels);

        var error = policy.Decide(Alert(AlertSeverity.Error), NotificationEventType.AlertCreated, 0);
        Assert.Equal(new[] { "Desktop" }, error.Channels);
    }

    [Fact]
    public void Escalation_rule_validation_rejects_invalid()
    {
        Assert.NotEmpty(new AlertEscalationRule { Level = 0, AfterDuration = TimeSpan.FromMinutes(1) }.Validate());
        Assert.NotEmpty(new AlertEscalationRule { Level = 1, AfterDuration = TimeSpan.Zero }.Validate());
        Assert.Empty(new AlertEscalationRule { Level = 1, AfterDuration = TimeSpan.FromMinutes(1) }.Validate());
    }
}

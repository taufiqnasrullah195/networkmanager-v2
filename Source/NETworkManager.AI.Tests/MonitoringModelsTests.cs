using NETworkManager.AI.Monitoring;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringModelsTests
{
    [Fact]
    public void Target_address_prefers_ip_then_hostname()
    {
        Assert.Equal("10.0.0.1", new MonitoringTarget { Id = "t", IPAddress = "10.0.0.1", Hostname = "host.local" }.Address);
        Assert.Equal("host.local", new MonitoringTarget { Id = "t", Hostname = "host.local" }.Address);
    }

    [Fact]
    public void Target_display_name_falls_back_to_address_then_id()
    {
        Assert.Equal("GW", new MonitoringTarget { Id = "t", Name = "GW", IPAddress = "10.0.0.1" }.DisplayName);
        Assert.Equal("10.0.0.1", new MonitoringTarget { Id = "t", IPAddress = "10.0.0.1" }.DisplayName);
        Assert.Equal("t", new MonitoringTarget { Id = "t" }.DisplayName);
    }

    [Fact]
    public void Profile_with_unknown_target_reference_is_invalid()
    {
        var profile = new MonitoringProfile
        {
            Name = "core",
            Checks = new[] { new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.Ping, TargetId = "missing" } },
        };

        var errors = profile.Validate();
        Assert.Contains(errors, e => e.Contains("unknown target 'missing'"));
    }

    [Fact]
    public void Profile_with_duplicate_ids_is_invalid()
    {
        var target = new MonitoringTarget { Id = "t" };

        var profile = new MonitoringProfile
        {
            Name = "core",
            Targets = new[] { target, target with { Name = "dup" } },
        };

        Assert.Contains(profile.Validate(), e => e.Contains("Duplicate target id"));
    }

    [Fact]
    public void Tcp_check_with_bad_port_is_invalid()
    {
        var profile = new MonitoringProfile
        {
            Name = "core",
            Targets = new[] { new MonitoringTarget { Id = "t" } },
            Checks = new[]
            {
                new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.TcpConnectivity, TargetId = "t", Port = 70000 },
            },
        };

        Assert.Contains(profile.Validate(), e => e.Contains("port"));
    }

    [Fact]
    public void Options_reject_extreme_values()
    {
        var errors = new MonitoringOptions
        {
            DefaultInterval = TimeSpan.Zero,
            DefaultTimeout = TimeSpan.FromHours(2),
            MaxConcurrency = 1000,
        }.Validate();

        Assert.Equal(3, errors.Count);
    }
}
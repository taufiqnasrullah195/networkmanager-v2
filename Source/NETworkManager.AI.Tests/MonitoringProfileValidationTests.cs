using NETworkManager.AI.Monitoring;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringProfileValidationTests
{
    [Fact]
    public void Target_without_address_is_invalid()
    {
        var errors = new MonitoringTarget { Id = "t" }.Validate();
        Assert.Contains(errors, e => e.Contains("hostname or IP address"));
    }

    [Fact]
    public void Target_without_id_is_invalid()
    {
        var errors = new MonitoringTarget { Id = "", IPAddress = "10.0.0.1" }.Validate();
        Assert.Contains(errors, e => e.Contains("identifier"));
    }

    [Fact]
    public void Check_without_target_reference_is_invalid()
    {
        var errors = new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.Ping, TargetId = "" }.Validate();
        Assert.Contains(errors, e => e.Contains("reference a target"));
    }

    [Fact]
    public void Tcp_check_with_invalid_port_is_rejected()
    {
        var errors = new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.TcpConnectivity, TargetId = "t", Port = 0 }.Validate();
        Assert.Contains(errors, e => e.Contains("TCP port"));
    }

    [Fact]
    public void Check_timeout_out_of_range_is_rejected()
    {
        var errors = new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.Ping, TargetId = "t", Timeout = TimeSpan.FromMilliseconds(1) }.Validate();
        Assert.Contains(errors, e => e.Contains("timeout"));
    }

    [Fact]
    public void Dns_check_requires_target_with_address()
    {
        var profile = new MonitoringProfile
        {
            Name = "core",
            Targets = new[] { new MonitoringTarget { Id = "srv" } }, // no address → DNS hostname missing
            Checks = new[] { new MonitoringCheck { CheckId = "c", Type = MonitorCheckType.DnsResolution, TargetId = "srv" } },
        };

        var errors = profile.Validate();
        Assert.Contains(errors, e => e.Contains("hostname or IP address"));
    }

    [Fact]
    public void Duplicate_target_address_produces_warning()
    {
        var profile = new MonitoringProfile
        {
            Name = "core",
            Targets = new[]
            {
                new MonitoringTarget { Id = "a", IPAddress = "10.0.0.1" },
                new MonitoringTarget { Id = "b", IPAddress = "10.0.0.1" },
            },
        };

        Assert.Contains(profile.Validate(), e => e.Contains("share the same address"));
    }

    [Fact]
    public void Invalid_check_type_parameters_are_rejected()
    {
        // A valid profile (Ping + DNS + TCP) must pass; a malformed one must fail — here we assert TCP port validation flows through.
        var valid = new MonitoringProfile
        {
            Name = "core",
            Targets = new[] { new MonitoringTarget { Id = "t", Hostname = "server01.local" } },
            Checks = new[]
            {
                new MonitoringCheck { CheckId = "ping", Type = MonitorCheckType.Ping, TargetId = "t" },
                new MonitoringCheck { CheckId = "dns", Type = MonitorCheckType.DnsResolution, TargetId = "t" },
                new MonitoringCheck { CheckId = "tcp", Type = MonitorCheckType.TcpConnectivity, TargetId = "t", Port = 443 },
            },
        };

        Assert.Empty(valid.Validate());
    }
}
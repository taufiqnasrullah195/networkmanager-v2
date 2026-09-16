using NETworkManager.AI.Models;
using Xunit;

namespace NETworkManager.AI.Tests;

public class ToolInputValidationTests
{
    [Fact]
    public void PingInput_empty_target_is_invalid()
    {
        var errors = new PingInput { Target = " " }.Validate();
        Assert.Contains(errors, e => e.Contains("Target"));
    }

    [Fact]
    public void PingInput_bounds_are_checked()
    {
        Assert.NotEmpty(new PingInput { Target = "1.1.1.1", Count = 0 }.Validate());
        Assert.NotEmpty(new PingInput { Target = "1.1.1.1", Count = 101 }.Validate());
        Assert.NotEmpty(new PingInput { Target = "1.1.1.1", TimeoutMilliseconds = 50 }.Validate());
        Assert.NotEmpty(new PingInput { Target = "1.1.1.1", TimeoutMilliseconds = 60001 }.Validate());
        Assert.Empty(new PingInput { Target = "1.1.1.1" }.Validate());
    }

    [Fact]
    public void TcpTestInput_port_range_is_enforced()
    {
        Assert.NotEmpty(new TcpTestInput { Host = "h", Port = 0 }.Validate());
        Assert.NotEmpty(new TcpTestInput { Host = "h", Port = 65536 }.Validate());
        Assert.Empty(new TcpTestInput { Host = "h", Port = 443 }.Validate());
    }

    [Fact]
    public void TracerouteInput_hop_and_timeout_bounds_are_enforced()
    {
        Assert.NotEmpty(new TracerouteInput { Target = "1.1.1.1", MaximumHops = 0 }.Validate());
        Assert.NotEmpty(new TracerouteInput { Target = "1.1.1.1", MaximumHops = 65 }.Validate());
        Assert.Empty(new TracerouteInput { Target = "1.1.1.1", MaximumHops = 30 }.Validate());
    }

    [Fact]
    public void DnsLookupInput_empty_host_is_invalid()
    {
        Assert.NotEmpty(new DnsLookupInput { Host = "" }.Validate());
        Assert.Empty(new DnsLookupInput { Host = "example.com" }.Validate());
    }

    [Fact]
    public void ReadOnlyInputs_are_always_valid()
    {
        Assert.Empty(new NetworkAdapterInput().Validate());
        Assert.Empty(new RoutingTableInput().Validate());
    }
}
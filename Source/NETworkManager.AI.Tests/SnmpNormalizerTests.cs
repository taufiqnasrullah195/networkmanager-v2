using NETworkManager.AI.Snmp;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SnmpNormalizerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private readonly SnmpNormalizer _normalizer = new();

    [Fact]
    public void System_variables_are_normalized()
    {
        var device = _normalizer.NormalizeSystem("sw01", "10.0.0.1", Now, SnmpTestData.System(
            name: "SW01", desc: "Cisco IOS", oid: "1.3.6.1.4.1.9.1.1000", uptimeHundredths: 1_209_600), reachable: true, responseTime: TimeSpan.FromMilliseconds(12));

        Assert.Equal("SW01", device.SysName);
        Assert.Equal("Cisco IOS", device.SysDescription);
        Assert.Equal("1.3.6.1.4.1.9.1.1000", device.SysObjectId);
        Assert.Equal(TimeSpan.FromMilliseconds(1_209_600 * 10), device.Uptime);
        Assert.True(device.Reachable);
    }

    [Fact]
    public void Missing_system_oid_is_null_not_fabricated()
    {
        var device = _normalizer.NormalizeSystem("sw01", "10.0.0.1", Now, [SnmpTestData.Var(SnmpNormalizer.SysName, "SW01")],
            reachable: true, responseTime: TimeSpan.Zero);

        Assert.Null(device.SysDescription);
        Assert.Null(device.SysObjectId);
        Assert.Null(device.Uptime);
    }

    [Fact]
    public void Interface_table_is_normalized()
    {
        var interfaces = _normalizer.NormalizeInterfaces("sw01", Now, SnmpTestData.Interfaces(
            (1, "1", "1000", "2000"),
            (2, "2", "0", "0")));

        Assert.Equal(2, interfaces.Count);

        var up = interfaces.Single(i => i.InterfaceIndex == 1);
        Assert.Equal("Gi1/0/1", up.Name);
        Assert.Equal(SnmpInterfaceAdminStatus.Up, up.AdminStatus);
        Assert.Equal(SnmpInterfaceOperStatus.Up, up.OperationalStatus);
        Assert.Equal(1000ul, up.InOctets);
        Assert.Equal(2000ul, up.OutOctets);
        Assert.Equal(1_000_000_000ul, up.SpeedBitsPerSecond);
        Assert.False(up.UsesHighCapacityCounters);

        var down = interfaces.Single(i => i.InterfaceIndex == 2);
        Assert.Equal(SnmpInterfaceOperStatus.Down, down.OperationalStatus);
    }

    [Fact]
    public void High_capacity_counters_are_preferred()
    {
        var combined = SnmpTestData.Interfaces((1, "1", "1000", "2000"))
            .Concat(SnmpTestData.IfX((1, "5000000000", "6000000000")))
            .ToArray();

        var interfaces = _normalizer.NormalizeInterfaces("sw01", Now, combined);

        var iface = Assert.Single(interfaces);
        Assert.True(iface.UsesHighCapacityCounters);
        Assert.Equal(5_000_000_000ul, iface.InOctets);
        Assert.Equal(6_000_000_000ul, iface.OutOctets);
    }

    [Fact]
    public void Missing_counters_are_null()
    {
        var variables = new[]
        {
            SnmpTestData.Var($"{SnmpNormalizer.IfDescr}.7", "lo0"),
            SnmpTestData.Var($"{SnmpNormalizer.IfAdminStatus}.7", "1"),
            SnmpTestData.Var($"{SnmpNormalizer.IfOperStatus}.7", "1"),
        };

        var iface = Assert.Single(_normalizer.NormalizeInterfaces("sw01", Now, variables));

        Assert.Null(iface.InOctets);
        Assert.Null(iface.OutOctets);
        Assert.Null(iface.SpeedBitsPerSecond);
    }

    [Theory]
    [InlineData("1", SnmpInterfaceOperStatus.Up)]
    [InlineData("2", SnmpInterfaceOperStatus.Down)]
    [InlineData("3", SnmpInterfaceOperStatus.Testing)]
    [InlineData("5", SnmpInterfaceOperStatus.Dormant)]
    [InlineData("7", SnmpInterfaceOperStatus.LowerLayerDown)]
    public void Operational_status_is_normalized(string value, SnmpInterfaceOperStatus expected)
    {
        var variables = new[]
        {
            SnmpTestData.Var($"{SnmpNormalizer.IfDescr}.1", "Gi1/0/1"),
            SnmpTestData.Var($"{SnmpNormalizer.IfOperStatus}.1", value),
        };

        var iface = Assert.Single(_normalizer.NormalizeInterfaces("sw01", Now, variables));
        Assert.Equal(expected, iface.OperationalStatus);
    }

    [Fact]
    public void Unknown_status_code_maps_to_unknown()
    {
        var variables = new[]
        {
            SnmpTestData.Var($"{SnmpNormalizer.IfDescr}.1", "Gi1/0/1"),
            SnmpTestData.Var($"{SnmpNormalizer.IfOperStatus}.1", "99"),
        };

        var iface = Assert.Single(_normalizer.NormalizeInterfaces("sw01", Now, variables));
        Assert.Equal(SnmpInterfaceOperStatus.Unknown, iface.OperationalStatus);
    }
}

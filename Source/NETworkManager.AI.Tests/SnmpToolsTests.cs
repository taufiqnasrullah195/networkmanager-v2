using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Snmp;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class SnmpToolsTests
{
    [Fact]
    public async Task Device_telemetry_tool_returns_structured_data()
    {
        var repo = new FakeSnmpTelemetryRepository
        {
            LatestDevice = new DeviceTelemetry
            {
                DeviceId = "sw01",
                Target = "10.0.0.1",
                Timestamp = DateTimeOffset.UtcNow,
                SysName = "SW01",
                Uptime = TimeSpan.FromDays(14),
            },
        };

        var tool = new DeviceTelemetryTool(repo);
        var outcome = await tool.ExecuteAsync(new DeviceTelemetryInput { DeviceId = "sw01" }, new(), CancellationToken.None);

        var result = Assert.IsType<DeviceTelemetryResult>(outcome.Data);
        Assert.Equal("SW01", result.Device!.SysName);
        Assert.Equal(TimeSpan.FromDays(14), result.Device.Uptime);
    }

    [Fact]
    public async Task Interface_telemetry_tool_returns_structured_data()
    {
        var repo = new FakeSnmpTelemetryRepository
        {
            LatestInterfaces = new[]
            {
                new InterfaceTelemetry
                {
                    DeviceId = "sw01",
                    InterfaceIndex = 1,
                    Name = "Gi1/0/1",
                    OperationalStatus = SnmpInterfaceOperStatus.Down,
                    InOctets = 123,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            },
        };

        var tool = new InterfaceTelemetryTool(repo);
        var outcome = await tool.ExecuteAsync(new InterfaceTelemetryInput { DeviceId = "sw01" }, new(), CancellationToken.None);

        var result = Assert.IsType<InterfaceTelemetryResult>(outcome.Data);
        Assert.Single(result.Interfaces);
        Assert.Equal(SnmpInterfaceOperStatus.Down, result.Interfaces[0].OperationalStatus);
    }

    [Fact]
    public void Input_validation_rejects_missing_device_id()
    {
        Assert.NotEmpty(new DeviceTelemetryInput { DeviceId = null }.Validate());
        Assert.NotEmpty(new InterfaceTelemetryInput { DeviceId = "" }.Validate());
        Assert.Empty(new InterfaceTelemetryInput { DeviceId = "sw01" }.Validate());
    }

    [Fact]
    public void Input_validation_rejects_out_of_range_limit()
    {
        Assert.NotEmpty(new InterfaceTelemetryInput { DeviceId = "sw01", Limit = 0 }.Validate());
        Assert.NotEmpty(new InterfaceTelemetryInput { DeviceId = "sw01", Limit = 1001 }.Validate());
        Assert.Empty(new InterfaceTelemetryInput { DeviceId = "sw01", Limit = 100 }.Validate());
    }

    [Fact]
    public void Tools_are_read_only_and_depend_only_on_the_repository()
    {
        // The tools can only read history through ISnmpTelemetryRepository — no SNMP transport, no mutation surface.
        AssertToolsReadOnly<DeviceTelemetryTool>();
        AssertToolsReadOnly<InterfaceTelemetryTool>();

        // The repository abstraction exposes no SET/transport method.
        var methods = typeof(ISnmpTelemetryRepository).GetMethods().Select(m => m.Name).ToList();
        Assert.DoesNotContain(methods, m => m.Contains("Set", StringComparison.OrdinalIgnoreCase)
                                           || m.Contains("Walk", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertToolsReadOnly<T>() where T : INetworkTool
    {
        var ctor = typeof(T).GetConstructors().Single();
        var param = Assert.Single(ctor.GetParameters());
        Assert.Equal(typeof(ISnmpTelemetryRepository), param.ParameterType);

        var declared = typeof(T).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(m => m.DeclaringType == typeof(T))
            .Select(m => m.Name);
        Assert.DoesNotContain(declared, n => n.Contains("Set", StringComparison.OrdinalIgnoreCase)
                                             || n.Contains("Walk", StringComparison.OrdinalIgnoreCase));
    }
}

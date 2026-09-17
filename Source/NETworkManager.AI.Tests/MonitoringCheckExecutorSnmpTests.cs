using NETworkManager.AI.Execution;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Snmp;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class MonitoringCheckExecutorSnmpTests
{
    private static MonitoringCheck SnmpCheck(SnmpCheckConfig? snmp = null) => new()
    {
        CheckId = "sw01-snmp",
        Type = MonitorCheckType.SnmpTelemetry,
        TargetId = "sw01",
        Snmp = snmp ?? new SnmpCheckConfig { CredentialReference = "ref" },
    };

    private static MonitoringCheck SnmpCheckWithoutConfig() => new()
    {
        CheckId = "sw01-snmp",
        Type = MonitorCheckType.SnmpTelemetry,
        TargetId = "sw01",
        Snmp = null,
    };

    private static MonitoringTarget Target() => new() { Id = "sw01", IPAddress = "10.0.0.1" };

    [Fact]
    public async Task Snmp_success_maps_to_healthy_result_with_telemetry()
    {
        var collector = new FakeSnmpCollector
        {
            Result = new SnmpCollectionResult
            {
                TargetId = "sw01",
                Host = "10.0.0.1",
                Timestamp = DateTimeOffset.UtcNow,
                Status = SnmpCollectionStatus.Success,
                Device = new DeviceTelemetry { DeviceId = "sw01", Target = "10.0.0.1", Timestamp = DateTimeOffset.UtcNow, SysName = "SW01" },
            },
        };

        var executor = new MonitoringCheckExecutor(new ToolExecutionService(new ToolRegistry()), collector);
        var result = await executor.ExecuteAsync(SnmpCheck(), Target());

        Assert.Equal(MonitoringResultStatus.Healthy, result.Status);
        Assert.Equal(MonitorCheckType.SnmpTelemetry, result.CheckType);
        Assert.IsType<SnmpCollectionResult>(result.Observed);
        Assert.Contains("SW01", result.SafeMessage);
        Assert.Equal("sw01", collector.LastTargetId);
        Assert.Equal("10.0.0.1", collector.LastHost);
    }

    [Fact]
    public async Task Snmp_timeout_maps_to_timeout_result()
    {
        var collector = new FakeSnmpCollector { Throw = new TimeoutException() };
        var executor = new MonitoringCheckExecutor(new ToolExecutionService(new ToolRegistry()), collector);

        var result = await executor.ExecuteAsync(SnmpCheck(), Target());

        Assert.Equal(MonitoringResultStatus.Timeout, result.Status);
        Assert.Equal(MonitorErrorClass.Timeout, result.ErrorClassification);
    }

    [Fact]
    public async Task Snmp_without_collector_returns_error()
    {
        var executor = new MonitoringCheckExecutor(new ToolExecutionService(new ToolRegistry()), snmpCollector: null);

        var result = await executor.ExecuteAsync(SnmpCheck(), Target());

        Assert.Equal(MonitoringResultStatus.Error, result.Status);
        Assert.Equal(MonitorErrorClass.ExecutionError, result.ErrorClassification);
    }

    [Fact]
    public async Task Snmp_missing_config_returns_error()
    {
        var executor = new MonitoringCheckExecutor(new ToolExecutionService(new ToolRegistry()), new FakeSnmpCollector());

        var result = await executor.ExecuteAsync(SnmpCheckWithoutConfig(), Target());

        Assert.Equal(MonitoringResultStatus.Error, result.Status);
        Assert.Equal(MonitorErrorClass.ExecutionError, result.ErrorClassification);
    }

    [Fact]
    public async Task Snmp_pre_cancelled_returns_cancelled()
    {
        var executor = new MonitoringCheckExecutor(new ToolExecutionService(new ToolRegistry()), new FakeSnmpCollector());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await executor.ExecuteAsync(SnmpCheck(), Target(), cts.Token);

        Assert.Equal(MonitoringResultStatus.Cancelled, result.Status);
    }

    [Fact]
    public void Check_validation_requires_snmp_config_for_snmp_check()
    {
        var missing = SnmpCheckWithoutConfig();
        Assert.Contains(missing.Validate(), e => e.Contains("SNMP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Snmp_check_without_credential_reference_is_invalid()
    {
        var check = SnmpCheck(new SnmpCheckConfig { CredentialReference = null });
        Assert.Contains(check.Validate(), e => e.Contains("credential reference", StringComparison.OrdinalIgnoreCase));
    }
}

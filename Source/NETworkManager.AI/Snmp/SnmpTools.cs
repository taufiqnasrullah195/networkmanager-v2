using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Snmp;

/// <summary>Input for the read-only <c>network_device_telemetry</c> tool.</summary>
public sealed record DeviceTelemetryInput : IValidatableToolInput
{
    public string? DeviceId { get; init; }

    public IReadOnlyList<string> Validate()
    {
        return string.IsNullOrWhiteSpace(DeviceId)
            ? new List<string> { "DeviceId is required." }
            : Array.Empty<string>();
    }
}

/// <summary>Output of <c>network_device_telemetry</c> — the latest normalized device telemetry (secret-free).</summary>
public sealed record DeviceTelemetryResult(DeviceTelemetry? Device, DateTimeOffset GeneratedAt);

/// <summary>Input for the read-only <c>network_interface_telemetry</c> tool.</summary>
public sealed record InterfaceTelemetryInput : IValidatableToolInput
{
    public string? DeviceId { get; init; }

    public int Limit { get; init; } = 100;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DeviceId))
            errors.Add("DeviceId is required.");
        if (Limit is < 1 or > 1000)
            errors.Add("Limit must be between 1 and 1000.");
        return errors;
    }
}

/// <summary>Output of <c>network_interface_telemetry</c> — latest interface telemetry rows (secret-free).</summary>
public sealed record InterfaceTelemetryResult(IReadOnlyList<InterfaceTelemetry> Interfaces, DateTimeOffset GeneratedAt);

/// <summary>
///     <c>network_device_telemetry</c> — read-only tool exposing the latest device telemetry to the AI. Depends solely
///     on <see cref="ISnmpTelemetryRepository"/>; it cannot perform a GET/WALK or SET against a device itself.
/// </summary>
public sealed class DeviceTelemetryTool : INetworkTool
{
    private readonly ISnmpTelemetryRepository _repository;

    public DeviceTelemetryTool(ISnmpTelemetryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public string Name => "network_device_telemetry";
    public string Description => "Reads the latest SNMP device telemetry (sysName, description, uptime, reachability) for a monitored device (read-only).";
    public ToolCategory Category => ToolCategory.Snmp;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public Type InputType => typeof(DeviceTelemetryInput);
    public Type OutputType => typeof(DeviceTelemetryResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = input as DeviceTelemetryInput ?? new DeviceTelemetryInput();

        var device = await _repository.GetLatestDeviceTelemetryAsync(query.DeviceId!, cancellationToken).ConfigureAwait(false);

        return ToolOutcome.Ok(new DeviceTelemetryResult(device, DateTimeOffset.UtcNow));
    }
}

/// <summary>
///     <c>network_interface_telemetry</c> — read-only tool exposing the latest interface telemetry to the AI. Depends
///     solely on <see cref="ISnmpTelemetryRepository"/>; it cannot perform a GET/WALK or SET against a device itself.
/// </summary>
public sealed class InterfaceTelemetryTool : INetworkTool
{
    private readonly ISnmpTelemetryRepository _repository;

    public InterfaceTelemetryTool(ISnmpTelemetryRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public string Name => "network_interface_telemetry";
    public string Description => "Reads the latest SNMP interface telemetry (status, counters, errors) for a monitored device (read-only).";
    public ToolCategory Category => ToolCategory.Snmp;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public Type InputType => typeof(InterfaceTelemetryInput);
    public Type OutputType => typeof(InterfaceTelemetryResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = input as InterfaceTelemetryInput ?? new InterfaceTelemetryInput();

        var interfaces = await _repository.GetLatestInterfaceTelemetryAsync(query.DeviceId!, query.Limit, cancellationToken).ConfigureAwait(false);

        return ToolOutcome.Ok(new InterfaceTelemetryResult(interfaces, DateTimeOffset.UtcNow));
    }
}

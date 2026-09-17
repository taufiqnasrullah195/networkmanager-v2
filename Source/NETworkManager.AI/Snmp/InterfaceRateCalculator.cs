namespace NETworkManager.AI.Snmp;

/// <summary>A sample of interface octet counters used to compute a rate against a previous sample.</summary>
public sealed record InterfaceRateSample
{
    public required ulong InOctets { get; init; }

    public required ulong OutOctets { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Interface speed in bits/second; <c>null</c> when unavailable (then utilization is unavailable).</summary>
    public ulong? SpeedBitsPerSecond { get; init; }
}

/// <summary>The computed interface rate. A counter reset or an untrustworthy sample yields <see cref="Unavailable"/>.</summary>
public sealed record InterfaceRateResult
{
    public double? ReceiveBitsPerSecond { get; init; }

    public double? TransmitBitsPerSecond { get; init; }

    /// <summary>Receive rate / speed (0..100), or <c>null</c> when speed is unavailable.</summary>
    public double? ReceiveUtilizationPercent { get; init; }

    /// <summary>Transmit rate / speed (0..100), or <c>null</c> when speed is unavailable.</summary>
    public double? TransmitUtilizationPercent { get; init; }

    /// <summary>True when the current counter is lower than the previous one (device reboot / interface reset / rollover).</summary>
    public bool CounterReset { get; init; }

    public bool Unavailable { get; init; }

    public string? Reason { get; init; }
}

/// <summary>
///     Converts two counter samples into a rate. When the calculation cannot be trusted (counter reset, missing
///     sample, non-positive elapsed time) it returns <see cref="InterfaceRateResult.Unavailable"/> with a reason —
///     never a negative or fabricated rate.
/// </summary>
public interface IInterfaceRateCalculator
{
    InterfaceRateResult Calculate(InterfaceRateSample previous, InterfaceRateSample current);
}

public sealed class InterfaceRateCalculator : IInterfaceRateCalculator
{
    public InterfaceRateResult Calculate(InterfaceRateSample previous, InterfaceRateSample current)
    {
        // A lower counter means the device rebooted / the interface reset / a counter wrapped. Treat it as a reset and
        // wait for the next valid sample — do NOT produce a negative rate.
        if (current.InOctets < previous.InOctets || current.OutOctets < previous.OutOctets)
        {
            return new InterfaceRateResult
            {
                CounterReset = true,
                Unavailable = true,
                Reason = "Counter decreased (device reboot, interface reset, or counter rollover); waiting for the next valid sample.",
            };
        }

        var elapsedSeconds = (current.Timestamp - previous.Timestamp).TotalSeconds;

        if (elapsedSeconds <= 0)
        {
            return new InterfaceRateResult
            {
                Unavailable = true,
                Reason = "Sample timestamps are identical or out of order; elapsed time must be positive.",
            };
        }

        var rxBits = (current.InOctets - previous.InOctets) * 8.0 / elapsedSeconds;
        var txBits = (current.OutOctets - previous.OutOctets) * 8.0 / elapsedSeconds;

        return new InterfaceRateResult
        {
            ReceiveBitsPerSecond = rxBits,
            TransmitBitsPerSecond = txBits,
            ReceiveUtilizationPercent = Utilization(rxBits, current.SpeedBitsPerSecond),
            TransmitUtilizationPercent = Utilization(txBits, current.SpeedBitsPerSecond),
        };
    }

    private static double? Utilization(double rateBitsPerSecond, ulong? speedBitsPerSecond)
    {
        if (speedBitsPerSecond is not { } speed || speed == 0)
            return null;

        return rateBitsPerSecond / speed * 100.0;
    }
}

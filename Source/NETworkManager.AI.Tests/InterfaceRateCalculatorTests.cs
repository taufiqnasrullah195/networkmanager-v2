using NETworkManager.AI.Snmp;
using Xunit;

namespace NETworkManager.AI.Tests;

public class InterfaceRateCalculatorTests
{
    private readonly InterfaceRateCalculator _calculator = new();
    private static readonly DateTimeOffset T0 = new(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Rx_and_tx_rates_are_calculated()
    {
        var previous = Sample(1_000, 2_000, T0);
        var current = Sample(1_000 + 8_000_000, 2_000 + 4_000_000, T0.AddSeconds(1));

        var rate = _calculator.Calculate(previous, current);

        // 8_000_000 octets * 8 bits = 64 Mbit over 1s.
        Assert.Equal(64_000_000.0, rate.ReceiveBitsPerSecond!.Value, 0);
        Assert.Equal(32_000_000.0, rate.TransmitBitsPerSecond!.Value, 0);
        Assert.False(rate.Unavailable);
        Assert.False(rate.CounterReset);
    }

    [Fact]
    public void Utilization_is_computed_when_speed_is_known()
    {
        var previous = Sample(0, 0, T0, speed: 1_000_000_000);
        var current = Sample(100_000_000, 0, T0.AddSeconds(1), speed: 1_000_000_000);

        var rate = _calculator.Calculate(previous, current);

        // 100_000_000 octets * 8 = 800 Mbit/s over 1s on a 1 Gbit/s link = 80%.
        Assert.Equal(80.0, rate.ReceiveUtilizationPercent!.Value, 3);
    }

    [Fact]
    public void Utilization_is_unavailable_without_speed()
    {
        var previous = Sample(0, 0, T0, speed: null);
        var current = Sample(1000, 0, T0.AddSeconds(1), speed: null);

        var rate = _calculator.Calculate(previous, current);

        Assert.Null(rate.ReceiveUtilizationPercent);
        Assert.False(rate.Unavailable);
    }

    [Fact]
    public void Counter_decrease_is_a_reset_not_a_negative_rate()
    {
        var previous = Sample(5_000, 5_000, T0);
        var current = Sample(100, 100, T0.AddSeconds(1));

        var rate = _calculator.Calculate(previous, current);

        Assert.True(rate.CounterReset);
        Assert.True(rate.Unavailable);
        Assert.Null(rate.ReceiveBitsPerSecond);
        Assert.Contains("reset", rate.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Zero_or_negative_elapsed_time_is_unavailable()
    {
        var previous = Sample(100, 100, T0);
        var current = Sample(200, 200, T0);

        var rate = _calculator.Calculate(previous, current);

        Assert.True(rate.Unavailable);
        Assert.Null(rate.ReceiveBitsPerSecond);
    }

    [Fact]
    public void Equal_counters_yield_zero_rate()
    {
        var previous = Sample(1_000, 1_000, T0);
        var current = Sample(1_000, 1_000, T0.AddSeconds(5));

        var rate = _calculator.Calculate(previous, current);

        Assert.Equal(0.0, rate.ReceiveBitsPerSecond!.Value);
        Assert.Equal(0.0, rate.TransmitBitsPerSecond!.Value);
        Assert.False(rate.Unavailable);
    }

    private static InterfaceRateSample Sample(ulong inOctets, ulong outOctets, DateTimeOffset timestamp, ulong? speed = null) => new()
    {
        InOctets = inOctets,
        OutOctets = outOctets,
        Timestamp = timestamp,
        SpeedBitsPerSecond = speed,
    };
}

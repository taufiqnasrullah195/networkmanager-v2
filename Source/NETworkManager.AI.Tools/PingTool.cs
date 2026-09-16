using System.Net;
using System.Net.Sockets;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.Models.Network;

namespace NETworkManager.AI.Tools;

/// <summary>
///     <c>ping</c> tool. Wraps the NETworkManager <see cref="Ping"/> engine (a continuous monitor) and stops it after
///     the requested number of replies, returning aggregate latency and packet loss.
/// </summary>
public sealed class PingTool : INetworkTool
{
    public string Name => "ping";
    public string Description => "Tests ICMP connectivity to a target host and reports latency and packet loss.";
    public ToolCategory Category => ToolCategory.Connectivity;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);
    public Type InputType => typeof(PingInput);
    public Type OutputType => typeof(PingResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var pingInput = (PingInput)input!;

        IPAddress? target;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(pingInput.Target!, cancellationToken).ConfigureAwait(false);
            target = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? addresses.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            return ToolOutcome.Failed("Cancelled", "Ping was cancelled.");
        }
        catch (Exception ex)
        {
            return ToolOutcome.Failed("ResolutionFailed", $"Could not resolve host '{pingInput.Target}': {ex.Message}");
        }

        if (target is null)
            return ToolOutcome.Failed("ResolutionFailed", $"Could not resolve host '{pingInput.Target}'.");

        var roundTrips = new List<long>();
        var received = 0;
        var sent = 0;
        string? engineError = null;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ping = new Ping { Timeout = pingInput.TimeoutMilliseconds, WaitTime = 0 };

        using var stopCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopCts.Token);

        ping.PingReceived += (_, e) =>
        {
            sent++;

            if (e.Args.Status == System.Net.NetworkInformation.IPStatus.Success)
            {
                received++;
                roundTrips.Add(e.Args.Time);
            }

            if (sent >= pingInput.Count)
                stopCts.Cancel();
        };

        ping.PingException += (_, e) =>
        {
            engineError = e.Message;
            completion.TrySetResult();
        };

        ping.PingCompleted += (_, _) => completion.TrySetResult();
        ping.UserHasCanceled += (_, _) => completion.TrySetResult();

        ping.SendAsync(target, linkedCts.Token);

        await completion.Task.ConfigureAwait(false);

        if (engineError is not null)
            return ToolOutcome.Failed("PingFailed", engineError);

        var success = received > 0;
        var packetLoss = sent == 0 ? 100.0 : (sent - received) * 100.0 / sent;

        var result = new PingResult
        {
            Target = pingInput.Target!,
            Success = success,
            Sent = sent,
            Received = received,
            PacketLossPercent = packetLoss,
            MinLatencyMilliseconds = roundTrips.Count > 0 ? roundTrips.Min() : 0,
            MaxLatencyMilliseconds = roundTrips.Count > 0 ? roundTrips.Max() : 0,
            AverageLatencyMilliseconds = roundTrips.Count > 0 ? roundTrips.Average() : 0,
            RoundTripTimesMilliseconds = roundTrips.ToArray(),
        };

        return ToolOutcome.Ok(result);
    }
}
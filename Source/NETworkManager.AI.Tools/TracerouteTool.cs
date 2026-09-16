using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NETworkManager.Models.Network;

namespace NETworkManager.AI.Tools;

/// <summary>
///     <c>traceroute</c> tool. Wraps the NETworkManager <see cref="Traceroute"/> engine (IP geolocation disabled to keep
///     the diagnostic deterministic and offline).
/// </summary>
public sealed class TracerouteTool : INetworkTool
{
    public string Name => "traceroute";
    public string Description => "Discovers the network path to a target host (hop by hop).";
    public ToolCategory Category => ToolCategory.PathDiscovery;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(180);
    public Type InputType => typeof(TracerouteInput);
    public Type OutputType => typeof(TracerouteResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var tracerouteInput = (TracerouteInput)input!;

        IPAddress? target;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(tracerouteInput.Target!, cancellationToken).ConfigureAwait(false);
            target = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                     ?? addresses.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            return ToolOutcome.Failed("Cancelled", "Traceroute was cancelled.");
        }
        catch (Exception ex)
        {
            return ToolOutcome.Failed("ResolutionFailed", $"Could not resolve host '{tracerouteInput.Target}': {ex.Message}");
        }

        if (target is null)
            return ToolOutcome.Failed("ResolutionFailed", $"Could not resolve host '{tracerouteInput.Target}'.");

        var options = new TracerouteOptions(
            tracerouteInput.TimeoutMilliseconds,
            new byte[32],
            tracerouteInput.MaximumHops,
            dontFragment: true,
            resolveHostname: true,
            checkIPApiIPGeolocation: false);

        var traceroute = new Traceroute(options);

        var hops = new List<TracerouteHop>();
        string? error = null;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        traceroute.HopReceived += (_, e) =>
        {
            var info = e.Args;
            long? latency = null;

            if (info.Status1 is IPStatus.Success or IPStatus.TtlExpired)
                latency = info.Time1;
            else if (info.Status2 is IPStatus.Success or IPStatus.TtlExpired)
                latency = info.Time2;
            else if (info.Status3 is IPStatus.Success or IPStatus.TtlExpired)
                latency = info.Time3;

            hops.Add(new TracerouteHop(
                info.Hop,
                info.IPAddress?.ToString(),
                string.IsNullOrEmpty(info.Hostname) ? null : info.Hostname.TrimEnd('.'),
                info.Status1.ToString(),
                latency));
        };

        traceroute.TraceComplete += (_, _) => completion.TrySetResult();
        traceroute.MaximumHopsReached += (_, _) => completion.TrySetResult();
        traceroute.UserHasCanceled += (_, _) => completion.TrySetResult();
        traceroute.TraceError += (_, e) =>
        {
            error = e.ErrorMessage;
            completion.TrySetResult();
        };

        traceroute.TraceAsync(target, cancellationToken);

        await completion.Task.ConfigureAwait(false);

        if (error is not null)
            return ToolOutcome.Failed("TracerouteFailed", error);

        var hopList = hops.OrderBy(h => h.Hop).ToList();
        var reachedTarget = hopList.Count > 0 && hopList[^1].IpAddress == target.ToString();

        return ToolOutcome.Ok(new TracerouteResult
        {
            Target = tracerouteInput.Target!,
            Success = reachedTarget,
            Hops = hopList,
        });
    }
}
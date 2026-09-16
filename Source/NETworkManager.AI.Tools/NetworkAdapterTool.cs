using System.Net;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;
using NetInterface = NETworkManager.Models.Network.NetworkInterface;

namespace NETworkManager.AI.Tools;

/// <summary>
///     <c>network_adapter_info</c> tool. Wraps NETworkManager's <see cref="NetInterface.GetNetworkInterfacesAsync"/> to
///     expose a read-only adapter enumeration.
/// </summary>
public sealed class NetworkAdapterTool : INetworkTool
{
    public string Name => "network_adapter_info";
    public string Description => "Enumerates local network adapters and their read-only configuration.";
    public ToolCategory Category => ToolCategory.NetworkInterface;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);
    public Type InputType => typeof(NetworkAdapterInput);
    public Type OutputType => typeof(NetworkAdapterResult);

    public async Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var adapterInput = (NetworkAdapterInput)input!;

        try
        {
            var interfaces = await NetInterface.GetNetworkInterfacesAsync().ConfigureAwait(false);

            var filter = adapterInput.NameFilter;

            var adapters = interfaces
                .Where(i => string.IsNullOrWhiteSpace(filter)
                            || (i.Name ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Select(i => new NetworkAdapterInfo(
                    i.Id ?? string.Empty,
                    i.Name ?? string.Empty,
                    i.Description ?? string.Empty,
                    i.Type ?? string.Empty,
                    i.Status.ToString(),
                    i.Speed,
                    i.PhysicalAddress?.ToString(),
                    (i.IPv4Address ?? Array.Empty<Tuple<IPAddress, IPAddress>>())
                        .Select(a => $"{a.Item1} / {a.Item2}")
                        .ToArray(),
                    (i.IPv4Gateway ?? Array.Empty<IPAddress>())
                        .Select(g => g.ToString())
                        .ToArray(),
                    i.DhcpEnabled,
                    (i.DNSServer ?? Array.Empty<IPAddress>())
                        .Select(d => d.ToString())
                        .ToArray()))
                .ToList();

            return ToolOutcome.Ok(new NetworkAdapterResult { Success = true, Adapters = adapters });
        }
        catch (Exception ex)
        {
            return ToolOutcome.Failed("AdapterInfoFailed", ex.Message);
        }
    }
}
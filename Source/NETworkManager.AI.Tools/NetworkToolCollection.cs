using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Tools;

/// <summary>Provides the initial set of read-only diagnostic network tools.</summary>
public static class NetworkToolCollection
{
    public static IReadOnlyList<INetworkTool> All() => new INetworkTool[]
    {
        new PingTool(),
        new DnsLookupTool(),
        new TcpTestTool(),
        new TracerouteTool(),
        new NetworkAdapterTool(),
        new RoutingTableTool(),
    };

    public static void RegisterAll(IToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (var tool in All())
            registry.Register(tool);
    }
}
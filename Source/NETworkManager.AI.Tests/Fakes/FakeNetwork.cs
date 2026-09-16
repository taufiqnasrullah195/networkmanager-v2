using NETworkManager.AI.Models;
using NETworkManager.AI.Registry;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>Deterministic fake network tools + registry factory for diagnostic workflows. No real network needed.</summary>
public static class FakeNetwork
{
    public static FakeNetworkTool AdapterTool(bool active, bool hasIp, string? gateway = null) => new()
    {
        Name = "network_adapter_info",
        InputType = typeof(NetworkAdapterInput),
        Handler = (_, _, _) =>
        {
            var adapter = new NetworkAdapterInfo(
                "eth0",
                "Ethernet",
                "Test adapter",
                "Ethernet",
                active ? "Up" : "Down",
                1_000_000_000,
                "AA:BB:CC:DD:EE:FF",
                hasIp ? new[] { "192.168.1.100 / 255.255.255.0" } : Array.Empty<string>(),
                gateway is null ? Array.Empty<string>() : new[] { gateway },
                true,
                new[] { "8.8.8.8" });

            return Task.FromResult(ToolOutcome.Ok(new NetworkAdapterResult { Success = true, Adapters = new[] { adapter } }));
        },
    };

    public static FakeNetworkTool RoutingTool(bool hasDefaultRoute) => new()
    {
        Name = "routing_table",
        InputType = typeof(RoutingTableInput),
        Handler = (_, _, _) =>
        {
            var routes = hasDefaultRoute
                ? new[] { new RouteEntry("0.0.0.0", 0, "192.168.1.1", 1, 25, "netmgmt") }
                : Array.Empty<RouteEntry>();

            return Task.FromResult(ToolOutcome.Ok(new RoutingTableResult { Success = true, Routes = routes }));
        },
    };

    public static FakeNetworkTool PingTool(bool reachable) => new()
    {
        Name = "ping",
        InputType = typeof(PingInput),
        Handler = (input, _, _) =>
        {
            var ping = (PingInput)input!;

            return Task.FromResult(ToolOutcome.Ok(new PingResult
            {
                Target = ping.Target!,
                Success = reachable,
                Sent = 4,
                Received = reachable ? 4 : 0,
                PacketLossPercent = reachable ? 0 : 100,
                MinLatencyMilliseconds = reachable ? 1 : 0,
                MaxLatencyMilliseconds = reachable ? 3 : 0,
                AverageLatencyMilliseconds = reachable ? 2 : 0,
                RoundTripTimesMilliseconds = reachable ? new long[] { 1, 2, 3, 2 } : Array.Empty<long>(),
            }));
        },
    };

    public static FakeNetworkTool DnsTool(bool success) => new()
    {
        Name = "dns_lookup",
        InputType = typeof(DnsLookupInput),
        Handler = (_, _, _) => success
            ? Task.FromResult(ToolOutcome.Ok(new DnsLookupResult
            {
                Host = "example.com",
                Success = true,
                Records = new[] { new DnsRecord("example.com", 60, "A", "93.184.216.34") },
            }))
            : Task.FromResult(ToolOutcome.Failed("DnsLookupFailed", "No DNS records returned.")),
    };

    public static FakeNetworkTool TcpTool(bool reachable) => new()
    {
        Name = "tcp_test",
        InputType = typeof(TcpTestInput),
        Handler = (input, _, _) =>
        {
            var tcp = (TcpTestInput)input!;

            return Task.FromResult(ToolOutcome.Ok(new TcpTestResult
            {
                Host = tcp.Host!,
                Port = tcp.Port,
                Reachable = reachable,
                State = reachable ? "Open" : "Closed",
            }));
        },
    };

    public static FakeNetworkTool TracerouteTool(bool reachable) => new()
    {
        Name = "traceroute",
        InputType = typeof(TracerouteInput),
        Handler = (input, _, _) =>
        {
            var tr = (TracerouteInput)input!;

            return Task.FromResult(ToolOutcome.Ok(new TracerouteResult
            {
                Target = tr.Target!,
                Success = reachable,
                Hops = Array.Empty<TracerouteHop>(),
            }));
        },
    };

    /// <summary>Builds a registry populated with all six diagnostic tools in a given state.</summary>
    public static ToolRegistry Registry(
        bool adapterActive = true,
        bool hasIp = true,
        bool hasRoute = true,
        bool gatewayReachable = true,
        bool dnsOk = true,
        bool tcpOk = true,
        bool tracerouteOk = true,
        string? gateway = "192.168.1.1")
    {
        var registry = new ToolRegistry();

        registry.Register(AdapterTool(adapterActive, hasIp, gateway));
        registry.Register(RoutingTool(hasRoute));
        registry.Register(PingTool(gatewayReachable)); // shared by gateway_ping and external_ping
        registry.Register(DnsTool(dnsOk));
        registry.Register(TcpTool(tcpOk));
        registry.Register(TracerouteTool(tracerouteOk));

        return registry;
    }
}
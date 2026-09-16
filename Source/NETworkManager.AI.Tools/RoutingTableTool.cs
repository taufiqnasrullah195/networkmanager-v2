using System.Net;
using System.Runtime.InteropServices;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Tools;

/// <summary>
///     <c>routing_table</c> tool. Reads the local IPv4 routing table via the Win32 <c>GetIpForwardTable</c> API
///     (read-only; no shell execution). IPv6 is not yet implemented and returns a structured error.
/// </summary>
public sealed class RoutingTableTool : INetworkTool
{
    public string Name => "routing_table";
    public string Description => "Reads the local IPv4 routing table (read-only).";
    public ToolCategory Category => ToolCategory.Routing;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(15);
    public Type InputType => typeof(RoutingTableInput);
    public Type OutputType => typeof(RoutingTableResult);

    public Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var routingInput = (RoutingTableInput)input!;

        if (routingInput.AddressFamily == IPAddressFamily.IPv6)
            return Task.FromResult(ToolOutcome.Failed("NotImplemented", "IPv6 routing table is not implemented yet (IPv4 only)."));

        try
        {
            var routes = ReadIPv4RouteTable();
            return Task.FromResult(ToolOutcome.Ok(new RoutingTableResult { Success = true, Routes = routes }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolOutcome.Failed("RoutingTableFailed", ex.Message));
        }
    }

    private static IReadOnlyList<RouteEntry> ReadIPv4RouteTable()
    {
        var size = 0;
        var result = NativeMethods.GetIpForwardTable(IntPtr.Zero, ref size, false);

        if (result != NativeMethods.ErrorInsufficientBuffer)
            throw new InvalidOperationException($"GetIpForwardTable (size query) failed with error {result}.");

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            result = NativeMethods.GetIpForwardTable(buffer, ref size, false);
            if (result != NativeMethods.NoError)
                throw new InvalidOperationException($"GetIpForwardTable failed with error {result}.");

            var entryCount = Marshal.ReadInt32(buffer, 0);
            var rowSize = Marshal.SizeOf<NativeMethods.MibIpForwardRow>();
            var routes = new List<RouteEntry>(entryCount);

            for (var i = 0; i < entryCount; i++)
            {
                var row = Marshal.PtrToStructure<NativeMethods.MibIpForwardRow>(IntPtr.Add(buffer, 4 + i * rowSize));

                routes.Add(new RouteEntry(
                    Ipv4ToString(row.ForwardDest),
                    MaskToPrefixLength(row.ForwardMask),
                    Ipv4ToString(row.ForwardNextHop),
                    (int)row.ForwardIfIndex,
                    row.ForwardMetric1,
                    ProtocolToString(row.ForwardProto)));
            }

            return routes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string Ipv4ToString(uint value)
    {
        var hostOrder = unchecked((uint)IPAddress.NetworkToHostOrder(unchecked((int)value)));
        return new IPAddress(hostOrder).ToString();
    }

    private static int MaskToPrefixLength(uint mask)
    {
        var hostOrder = unchecked((uint)IPAddress.NetworkToHostOrder(unchecked((int)mask)));
        var v = hostOrder;
        var bits = 0;

        while (v != 0)
        {
            bits += (int)(v & 1);
            v >>= 1;
        }

        return bits;
    }

    private static string ProtocolToString(uint protocol) => protocol switch
    {
        2 => "local",
        3 => "netmgmt",
        4 => "icmp",
        8 => "rip",
        13 => "ospf",
        14 => "bgp",
        _ => $"proto({protocol})",
    };

    private static class NativeMethods
    {
        internal const uint NoError = 0;
        internal const uint ErrorInsufficientBuffer = 122;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MibIpForwardRow
        {
            public uint ForwardDest;
            public uint ForwardMask;
            public uint ForwardPolicy;
            public uint ForwardNextHop;
            public uint ForwardIfIndex;
            public uint ForwardType;
            public uint ForwardProto;
            public uint ForwardAge;
            public uint ForwardNextHopAs;
            public uint ForwardMetric1;
            public uint ForwardMetric2;
            public uint ForwardMetric3;
            public uint ForwardMetric4;
            public uint ForwardMetric5;
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        internal static extern uint GetIpForwardTable(IntPtr pIpForwardTable, ref int pdwSize, bool bOrder);
    }
}
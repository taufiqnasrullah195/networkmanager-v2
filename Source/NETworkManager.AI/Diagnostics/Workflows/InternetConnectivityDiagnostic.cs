using System.Text.Json;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Diagnostics.Workflows;

/// <summary>Read-only Internet Connectivity Diagnostic: adapter → IP → default route → gateway ping → DNS → external → TCP → traceroute.</summary>
public static class InternetConnectivityDiagnostic
{
    public const string WorkflowName = "Internet Connectivity Diagnostic";

    public static DiagnosticWorkflow Create() => new()
    {
        Name = WorkflowName,
        Description = "Determines where connectivity fails between the local machine and the Internet (read-only).",
        Severity = "MEDIUM",
        Timeout = TimeSpan.FromMinutes(3),
        Steps = new[]
        {
            AdapterStep(),
            IpConfigStep(),
            DefaultRouteStep(),
            GatewayPingStep(),
            DnsStep(),
            ExternalPingStep(),
            TcpStep(),
            TracerouteStep(),
        },
    };

    private static DiagnosticStep AdapterStep() => new()
    {
        Id = "adapter",
        Name = "Network adapters",
        Description = "Check whether at least one network adapter is active.",
        ToolName = "network_adapter_info",
        ArgumentsJson = "{}",
        ClassifiesAs = FailureClass.NoNetworkAdapter,
        Evaluator = (data, _) =>
            data is NetworkAdapterResult r && r.Adapters.Any(IsUp)
                ? StepEvaluation.Pass()
                : StepEvaluation.Fail("No active network adapter."),
    };

    private static DiagnosticStep IpConfigStep() => new()
    {
        Id = "ip_config",
        Name = "IP configuration",
        Description = "Check whether an active adapter has a valid IPv4 address.",
        ToolName = "network_adapter_info",
        ArgumentsJson = "{}",
        DependsOn = new[] { "adapter" },
        ClassifiesAs = FailureClass.NoIpAddress,
        Evaluator = (data, _) =>
            data is NetworkAdapterResult r && r.Adapters.Any(a => IsUp(a) && a.IPv4Addresses.Count > 0)
                ? StepEvaluation.Pass()
                : StepEvaluation.Fail("No IPv4 address on any active adapter."),
    };

    private static DiagnosticStep DefaultRouteStep() => new()
    {
        Id = "default_route",
        Name = "Default route",
        Description = "Check that a default route (0.0.0.0/0) exists.",
        ToolName = "routing_table",
        ArgumentsJson = "{}",
        DependsOn = new[] { "ip_config" },
        ClassifiesAs = FailureClass.NoDefaultGateway,
        Evaluator = (data, _) =>
            data is RoutingTableResult r && r.Routes.Any(IsDefaultRoute)
                ? StepEvaluation.Pass()
                : StepEvaluation.Fail("No default route (0.0.0.0/0) in the routing table."),
    };

    private static DiagnosticStep GatewayPingStep() => new()
    {
        Id = "gateway_ping",
        Name = "Gateway ping",
        Description = "Ping the default gateway.",
        ToolName = "ping",
        Arguments = ctx => JsonSerializer.Serialize(new { target = FindGateway(ctx), count = 4 }),
        DependsOn = new[] { "default_route" },
        ClassifiesAs = FailureClass.GatewayUnreachable,
        Evaluator = PingEvaluator,
    };

    private static DiagnosticStep DnsStep() => new()
    {
        Id = "dns_lookup",
        Name = "DNS lookup",
        Description = "Resolve a host name via DNS.",
        ToolName = "dns_lookup",
        Arguments = ctx => JsonSerializer.Serialize(new { host = ctx.Target.Hostname ?? "example.com" }),
        DependsOn = new[] { "gateway_ping" },
        ClassifiesAs = FailureClass.DnsFailure,
        Evaluator = (data, _) =>
            data is DnsLookupResult d && d.Success && d.Records.Count > 0
                ? StepEvaluation.Pass()
                : StepEvaluation.Fail("DNS resolution failed."),
    };

    private static DiagnosticStep ExternalPingStep() => new()
    {
        Id = "external_ping",
        Name = "External IP connectivity",
        Description = "Ping an external IP address beyond the gateway.",
        ToolName = "ping",
        Arguments = ctx => JsonSerializer.Serialize(new { target = ctx.Target.IPAddress ?? "1.1.1.1", count = 4 }),
        DependsOn = new[] { "gateway_ping" },
        ClassifiesAs = FailureClass.ExternalConnectivityFailure,
        Evaluator = PingEvaluator,
    };

    private static DiagnosticStep TcpStep() => new()
    {
        Id = "tcp_test",
        Name = "TCP connectivity",
        Description = "Test a TCP connection to the target host/port.",
        ToolName = "tcp_test",
        Arguments = ctx => JsonSerializer.Serialize(new { host = ctx.Target.Hostname ?? ctx.Target.IPAddress ?? "1.1.1.1", port = ctx.Target.Port ?? 443 }),
        DependsOn = new[] { "gateway_ping" },
        ClassifiesAs = FailureClass.TcpConnectivityFailure,
        Evaluator = (data, _) =>
            data is TcpTestResult t && t.Reachable
                ? StepEvaluation.Pass()
                : StepEvaluation.Fail("Target host did not accept the TCP connection."),
    };

    private static DiagnosticStep TracerouteStep() => new()
    {
        Id = "traceroute",
        Name = "Traceroute",
        Description = "Trace the path to the target (where appropriate).",
        ToolName = "traceroute",
        Arguments = ctx => JsonSerializer.Serialize(new { target = ctx.Target.Hostname ?? ctx.Target.IPAddress ?? "1.1.1.1" }),
        Required = false,
        DependsOn = new[] { "gateway_ping" },
        ClassifiesAs = FailureClass.ExternalConnectivityFailure,
        Evaluator = (data, _) =>
            data is TracerouteResult t && t.Success
                ? StepEvaluation.Pass()
                : StepEvaluation.PassWithWarning("Traceroute did not reach the target."),
    };

    private static readonly DiagnosticStepEvaluator PingEvaluator = static (data, _) =>
        data is PingResult p && p.Success
            ? StepEvaluation.Pass()
            : StepEvaluation.Fail("Target did not respond to ping.");

    private static bool IsUp(NetworkAdapterInfo adapter) =>
        string.Equals(adapter.Status, "Up", StringComparison.OrdinalIgnoreCase);

    private static bool IsDefaultRoute(RouteEntry route) =>
        route.DestinationPrefix == "0.0.0.0" && route.PrefixLength == 0;

    private static string? FindGateway(DiagnosticStepContext context)
    {
        if (context.DataOf("ip_config") is NetworkAdapterResult adapters)
            return adapters.Adapters.FirstOrDefault(a => a.IPv4Gateways.Count > 0)?.IPv4Gateways[0];

        if (context.DataOf("default_route") is RoutingTableResult routes)
            return routes.Routes.FirstOrDefault(IsDefaultRoute)?.NextHop;

        return null;
    }
}
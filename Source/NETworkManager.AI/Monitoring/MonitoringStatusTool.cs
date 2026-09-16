using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Models;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     <c>network_monitoring_status</c> — read-only tool exposing current monitoring state to the AI. Returns
///     structured evidence only; it can never add/remove targets or change configuration (it depends solely on
///     <see cref="IMonitoringQuery"/>).
/// </summary>
public sealed class MonitoringStatusTool : INetworkTool
{
    private readonly IMonitoringQuery _query;

    public MonitoringStatusTool(IMonitoringQuery query)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    public string Name => "network_monitoring_status";
    public string Description => "Reads current health and latest check results of monitored network targets (read-only).";
    public ToolCategory Category => ToolCategory.Monitoring;
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;
    public bool RequiresApproval => false;
    public TimeSpan Timeout => TimeSpan.FromSeconds(5);
    public Type InputType => typeof(MonitoringStatusInput);
    public Type OutputType => typeof(MonitoringStatusResult);

    public Task<ToolOutcome> ExecuteAsync(object? input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var query = input as MonitoringStatusInput ?? new MonitoringStatusInput();

        var all = _query.GetCurrentStatus();

        var snapshots = string.IsNullOrWhiteSpace(query.TargetId)
            ? all
            : all.Where(t => string.Equals(t.TargetId, query.TargetId, StringComparison.OrdinalIgnoreCase)).ToList();

        var byId = all.ToDictionary(t => t.TargetId, t => t.DisplayName, StringComparer.OrdinalIgnoreCase);

        var targets = snapshots
            .Select(s => new MonitoringTargetStatus(
                s.TargetId,
                s.DisplayName,
                s.Health.ToString(),
                s.Results
                    .Select(r => new MonitoringCheckStatus(
                        r.CheckType.ToString(),
                        r.Status.ToString(),
                        DescribeObserved(r),
                        r.Timestamp))
                    .ToList()))
            .ToList();

        var failures = _query.GetRecentFailures(query.MaxFailures)
            .Select(f => new MonitoringFailure(
                f.TargetId,
                byId.TryGetValue(f.TargetId, out var name) ? name : f.TargetId,
                f.CheckType.ToString(),
                f.Status.ToString(),
                f.SafeMessage,
                f.Timestamp))
            .ToList();

        return Task.FromResult(ToolOutcome.Ok(new MonitoringStatusResult(
            targets,
            failures,
            DateTimeOffset.UtcNow)));
    }

    private static string? DescribeObserved(MonitoringResult result) => result.Observed switch
    {
        PingResult ping => $"{ping.AverageLatencyMilliseconds:0} ms avg | {ping.PacketLossPercent:0}% loss",
        TcpTestResult tcp => $"port {tcp.Port}: {tcp.State}",
        DnsLookupResult dns => string.Join(", ", dns.Records.Where(r => r.RecordType is "A" or "AAAA").Select(r => r.Result)),
        _ => null,
    };
}
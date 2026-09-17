using System;
using System.Collections.Generic;
using NETworkManager.AI.Dashboard;
using NETworkManager.AI.Monitoring;

namespace NETworkManager;

/// <summary>
///     Composition root for the Network Health Dashboard (Step 14). Aggregates the existing monitoring/alert/history/SNMP
///     services into a single <see cref="DashboardAggregator"/>. The dashboard owns no domain logic — it only reads.
/// </summary>
public static class DashboardComposition
{
    private static DashboardAggregator? _aggregator;

    public static DashboardAggregator Aggregator => _aggregator ??= new DashboardAggregator(
        MonitoringComposition.Instance,
        AlertComposition.Alerts,
        PersistenceComposition.History,
        PersistenceComposition.History,
        PersistenceComposition.SnmpTelemetryRepository,
        BuildTargets(),
        () => MonitoringComposition.Instance.IsRunning);

    /// <summary>Flattens all profile targets into an id → target map for device-table enrichment (type/address).</summary>
    public static IReadOnlyDictionary<string, MonitoringTarget> BuildTargets()
    {
        var profiles = MonitoringComposition.ProfileService.GetProfilesAsync().GetAwaiter().GetResult();
        var targets = new Dictionary<string, MonitoringTarget>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            foreach (var target in profile.Targets)
                targets.TryAdd(target.Id, target);
        }

        return targets;
    }
}

/// <summary>Carries a pending copilot prompt from the dashboard to the AI copilot view (a contextual handoff).</summary>
public static class AiCopilotHandoff
{
    public static string? PendingPrompt { get; set; }
}

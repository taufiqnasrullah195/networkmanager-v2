using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Monitoring;

/// <summary>
///     Bridges configuration → engine. Replaces the engine's targets/checks with the enabled profiles, resolving
///     per-check interval/timeout from the profile defaults. Must be called while monitoring is stopped (hot-reload
///     is intentionally not implemented).
/// </summary>
public static class MonitoringConfigurationApplier
{
    public static void Apply(IMonitoringEngine engine, IReadOnlyList<MonitoringProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(profiles);

        engine.Clear();

        foreach (var profile in profiles.Where(p => p.Enabled))
        {
            foreach (var target in profile.Targets)
                engine.AddTarget(target);

            foreach (var check in profile.Checks)
                engine.AddCheck(Resolve(profile, check));
        }
    }

    /// <summary>Resolves a check's nullable interval/timeout from its profile's defaults (exposed for testing).</summary>
    public static MonitoringCheck Resolve(MonitoringProfile profile, MonitoringCheck check) =>
        check with
        {
            Timeout = check.Timeout ?? profile.DefaultTimeout,
            Interval = check.Interval ?? profile.DefaultInterval,
        };
}
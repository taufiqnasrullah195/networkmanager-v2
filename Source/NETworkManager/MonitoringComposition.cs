using System;
using System.IO;
using log4net;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Execution;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Registry;
using NETworkManager.AI.Tools;

namespace NETworkManager;

/// <summary>Bridges the monitoring engine's lifecycle/transition logger to log4net (ids/statuses only — no secrets).</summary>
public sealed class Log4netMonitoringLogger : IMonitoringLogger
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(MonitoringEngine));

    public void MonitoringStarted() => Log.Info("Network monitoring started.");
    public void MonitoringStopped() => Log.Info("Network monitoring stopped.");
    public void TargetAdded(string targetId) => Log.Info($"Monitoring target added: '{targetId}'.");
    public void TargetRemoved(string targetId) => Log.Info($"Monitoring target removed: '{targetId}'.");
    public void TargetEnabled(string targetId, bool enabled) => Log.Info($"Monitoring target '{targetId}' {(enabled ? "enabled" : "disabled")}.");
    public void CheckScheduled(string checkId, string targetId) => Log.Debug($"Monitoring check '{checkId}' scheduled for target '{targetId}'.");
    public void HealthStateChanged(HealthStateChange change) => Log.Info($"Monitoring health changed: '{change.DisplayName}' {change.Previous} -> {change.New} ({change.Reason}).");
    public void MonitoringError(string checkId, string message) => Log.Warn($"Monitoring check '{checkId}' failed.");
}

/// <summary>
///     Manual composition root for the monitoring engine (Step 9), mirroring <see cref="AICopilotFactory"/>.
///     Builds the engine over the real read-only tool set, loads the externalized profile (targets/checks, no
///     secrets) from the user's data directory, and exposes a shared <see cref="MonitoringEngine"/> so the WPF
///     view and the AI copilot's <c>network_monitoring_status</c> tool read the same in-memory state.
/// </summary>
public static class MonitoringComposition
{
    private static readonly Lazy<MonitoringEngine> EngineInstance = new(CreateEngine);

    public static MonitoringEngine Engine => EngineInstance.Value;

    private static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NETworkManager",
            "AI");

    private static MonitoringEngine CreateEngine()
    {
        // Sensible defaults (30s interval, 5s timeout, 5 concurrent checks). Targets come from the profile, never code.
        var options = new MonitoringOptions();

        var registry = new ToolRegistry();
        NetworkToolCollection.RegisterAll(registry);

        var execution = new ToolExecutionService(registry);
        var executor = new MonitoringCheckExecutor(execution);

        var engine = new MonitoringEngine(options, executor, logger: new Log4netMonitoringLogger());

        var profileStore = new MonitoringProfileStore(Path.Combine(DataDirectory, "monitoring-profile.json"));

        var profile = profileStore.Load() ?? new MonitoringProfile { Name = "Default" };

        if (profile.Validate().Count != 0)
            profile = new MonitoringProfile { Name = "Default" };

        if (profile.Enabled)
            LoadProfile(engine, profile);

        return engine;
    }

    /// <summary>Materializes a profile into the engine (targets are registered before checks).</summary>
    public static void LoadProfile(MonitoringEngine engine, MonitoringProfile profile)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(profile);

        foreach (var target in profile.Targets)
            engine.AddTarget(target);

        foreach (var check in profile.Checks)
            engine.AddCheck(check);
    }
}
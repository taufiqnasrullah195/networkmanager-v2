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
///     Manual composition root for monitoring (Step 9/10). Owns the configuration service (JSON repository, backward
///     compatible with the Step 9 single-profile file) and the shared monitoring engine. Configuration is applied to
///     the engine only while stopped, via <see cref="MonitoringConfigurationApplier"/> — the engine starts empty.
/// </summary>
public static class MonitoringComposition
{
    private const int DefaultMaxConcurrency = 5;

    private static readonly Lazy<MonitoringProfileService> ProfileServiceInstance = new(CreateProfileService);

    private static readonly object Gate = new();

    private static MonitoringEngine? _engine;

    private static int _maxConcurrency = DefaultMaxConcurrency;

    /// <summary>The configuration service (profiles CRUD + validation). The UI edits configuration through this.</summary>
    public static MonitoringProfileService ProfileService => ProfileServiceInstance.Value;

    /// <summary>The shared monitoring engine (built lazily; empty until configuration is applied).</summary>
    public static MonitoringEngine Instance
    {
        get
        {
            lock (Gate)
                return _engine ??= CreateEngine(_maxConcurrency);
        }
    }

    /// <summary>Rebuilds the engine with a new global concurrency cap (call while stopped; discards runtime state).</summary>
    public static MonitoringEngine Rebuild(int maxConcurrency)
    {
        lock (Gate)
        {
            _maxConcurrency = maxConcurrency;
            _engine = CreateEngine(maxConcurrency);
            return _engine;
        }
    }

    private static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NETworkManager",
            "AI");

    private static MonitoringProfileService CreateProfileService()
    {
        var catalogPath = Path.Combine(DataDirectory, "monitoring-profiles.json");
        var legacyPath = Path.Combine(DataDirectory, "monitoring-profile.json");

        return new MonitoringProfileService(new JsonMonitoringProfileRepository(catalogPath, legacyPath));
    }

    private static MonitoringEngine CreateEngine(int maxConcurrency)
    {
        PersistenceComposition.InitializeAsync().GetAwaiter().GetResult();

        var options = new MonitoringOptions { MaxConcurrency = maxConcurrency };

        var registry = new ToolRegistry();
        NetworkToolCollection.RegisterAll(registry);

        var execution = new ToolExecutionService(registry);
        var executor = new MonitoringCheckExecutor(execution, SnmpComposition.Collector);

        return new MonitoringEngine(options, executor, store: PersistenceComposition.MonitoringStateStore, logger: new Log4netMonitoringLogger());
    }
}
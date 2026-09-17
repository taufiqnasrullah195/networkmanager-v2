using System;
using System.IO;
using System.Threading.Tasks;
using log4net;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Persistence;

namespace NETworkManager;

/// <summary>
///     Composition root for the persistent state store (Step 12). Initializes a single SQLite database in the user's
///     local application data, exposes the persistent monitoring/alert stores (used by the engines) and the history/
///     retention repositories (used by the history UI and the read-only AI tools). Degrades gracefully: if the
///     database cannot be initialized, the engines fall back to in-memory stores and an error is reported.
/// </summary>
public static class PersistenceComposition
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(PersistenceComposition));

    private static readonly object Gate = new();

    private static Task? _initialization;

    private static SqliteDatabase? _database;
    private static SqliteMonitoringStateStore? _monitoringStore;
    private static SqliteAlertStore? _alertStore;
    private static SqliteHistoryRepository? _history;
    private static SqliteRetentionService? _retention;
    private static string? _error;

    public static IMonitoringStateStore? MonitoringStateStore => _monitoringStore;

    public static IAlertStore? AlertStore => _alertStore;

    public static SqliteHistoryRepository? History => _history;

    public static SqliteRetentionService? Retention => _retention;

    /// <summary>Human-readable error when initialization failed, else null.</summary>
    public static string? InitializationError => _error;

    public static bool IsInitialized => _database is not null;

    /// <summary>Idempotent initialization; never throws (failures are reported via <see cref="InitializationError"/>).</summary>
    public static Task InitializeAsync()
    {
        lock (Gate)
            return _initialization ??= InitializeCoreAsync();
    }

    private static async Task InitializeCoreAsync()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NETworkManager",
            "AI",
            "thewisenetwork.db");

        try
        {
            var database = new SqliteDatabase(path);
            await database.InitializeAsync().ConfigureAwait(false);

            var alertStore = new SqliteAlertStore(database);
            await alertStore.LoadActiveAlertsAsync().ConfigureAwait(false);

            _database = database;
            _monitoringStore = new SqliteMonitoringStateStore(database);
            _alertStore = alertStore;
            _history = new SqliteHistoryRepository(database);
            _retention = new SqliteRetentionService(database);
            _error = null;
        }
        catch (Exception ex)
        {
            _error = "Monitoring history storage is unavailable.";
            Log.Warn("Failed to initialize monitoring persistence; continuing with in-memory state.", ex);
        }
    }

    /// <summary>Runs retention cleanup off the UI thread (best-effort, logged).</summary>
    public static async Task RunCleanupAsync()
    {
        if (_retention is null)
            return;

        try
        {
            await _retention.RunCleanupAsync(new RetentionPolicy()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warn("Monitoring retention cleanup failed.", ex);
        }
    }
}
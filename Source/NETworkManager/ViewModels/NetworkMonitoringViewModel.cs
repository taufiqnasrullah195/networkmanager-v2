using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using log4net;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Models;
using NETworkManager.Utilities;

namespace NETworkManager.ViewModels;

/// <summary>One row of the monitoring table — display fields only, no business logic.</summary>
public sealed class MonitoringRowViewModel
{
    public string TargetId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Health { get; init; } = string.Empty;

    public string LastCheck { get; init; } = "—";

    public string Observed { get; init; } = "—";

    public string LastError { get; init; } = string.Empty;
}

/// <summary>
///     Minimal monitoring view model: reads <see cref="IMonitoringEngine.GetCurrentStatus"/> and subscribes as an
///     observer to refresh on lifecycle/transition events (marshalled onto the dispatcher). Presentation only.
/// </summary>
public class NetworkMonitoringViewModel : ViewModelBase, IMonitoringObserver
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(NetworkMonitoringViewModel));

    private readonly IMonitoringEngine _engine;

    public ObservableCollection<MonitoringRowViewModel> Rows { get; } = [];

    public ICommand RefreshCommand { get; }

    private string _statusMessage = string.Empty;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public NetworkMonitoringViewModel(IMonitoringEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        RefreshCommand = new RelayCommand(_ => Refresh());
        _engine.Subscribe(this);
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            var rows = _engine.GetCurrentStatus()
                .Select(s => new MonitoringRowViewModel
                {
                    TargetId = s.TargetId,
                    DisplayName = s.DisplayName,
                    Health = s.Health.ToString(),
                    LastCheck = s.Results.Count == 0
                        ? "—"
                        : s.Results.Max(r => r.Timestamp).ToLocalTime().ToString("HH:mm:ss"),
                    Observed = SummarizeObserved(s),
                    LastError = LatestError(s),
                })
                .ToList();

            Rows.Clear();

            foreach (var row in rows)
                Rows.Add(row);

            StatusMessage = rows.Count == 0
                ? "No monitoring targets configured. Add a monitoring-profile.json to get started."
                : $"{rows.Count} target(s) monitored.";
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read monitoring status.", ex);
            StatusMessage = "Failed to read monitoring status.";
        }
    }

    public void OnMonitoringEvent(MonitoringEvent e)
    {
        // Events fire on the scheduler thread — marshal to the UI dispatcher before touching the collection.
        OnUi(Refresh);
    }

    public void Detach()
    {
        _engine.Unsubscribe(this);
    }

    private static string SummarizeObserved(MonitoringSnapshot snapshot)
    {
        var parts = snapshot.Results
            .Select(Summarize)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        return parts.Count == 0 ? "—" : string.Join("; ", parts);
    }

    private static string? Summarize(MonitoringResult result) => result.Observed switch
    {
        PingResult ping when result.Status == MonitoringResultStatus.Healthy => $"{ping.AverageLatencyMilliseconds:0} ms avg",
        TcpTestResult tcp => $"tcp:{tcp.Port} {tcp.State}",
        DnsLookupResult dns => string.Join(", ", dns.Records.Where(r => r.RecordType is "A" or "AAAA").Select(r => r.Result)),
        _ => result.Status.ToString(),
    };

    private static string LatestError(MonitoringSnapshot snapshot) =>
        snapshot.Results
            .Where(r => r.Status is MonitoringResultStatus.Unhealthy or MonitoringResultStatus.Timeout or MonitoringResultStatus.Error)
            .OrderByDescending(r => r.Timestamp)
            .Select(r => r.SafeMessage)
            .FirstOrDefault() ?? string.Empty;

    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
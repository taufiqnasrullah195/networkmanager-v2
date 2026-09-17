using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using log4net;
using NETworkManager.AI.Dashboard;
using NETworkManager.AI.Notifications;
using NETworkManager.Utilities;

namespace NETworkManager.ViewModels;

/// <summary>Read-only display row for one device in the dashboard table.</summary>
public sealed class DashboardDeviceRow
{
    public string DeviceId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string Health { get; init; } = string.Empty;
    public string Snmp { get; init; } = "—";
    public string Alerts { get; init; } = "0";
    public string LastCheck { get; init; } = "—";
    public bool IsStale { get; init; }
}

/// <summary>Read-only display row for an active alert.</summary>
public sealed class DashboardAlertRow
{
    public string Severity { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Occurrences { get; init; } = string.Empty;
    public string LastSeen { get; init; } = string.Empty;
}

/// <summary>Read-only display row for a recent network event.</summary>
public sealed class DashboardEventRow
{
    public string Time { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

/// <summary>Read-only display row for an interface issue.</summary>
public sealed class DashboardIssueRow
{
    public string Device { get; init; } = string.Empty;
    public string Interface { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
}

/// <summary>Read-only display row for latency.</summary>
public sealed class DashboardLatencyRow
{
    public string Target { get; init; } = string.Empty;
    public string Average { get; init; } = "—";
    public string PacketLoss { get; init; } = "—";
}

/// <summary>Read-only display row for availability.</summary>
public sealed class DashboardAvailabilityRow
{
    public string Target { get; init; } = string.Empty;
    public string Availability { get; init; } = "—";
}

/// <summary>Read-only display row for one notification delivery.</summary>
public sealed class DashboardNotificationRow
{
    public string Time { get; init; } = string.Empty;
    public string Alert { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public string Event { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Attempts { get; init; } = string.Empty;
}

/// <summary>
///     Presentation-only view model for the Network Health Dashboard (Step 14). Aggregates the existing services via
///     <see cref="DashboardAggregator"/> — no monitoring/alert/SNMP/AI logic lives here.
/// </summary>
public class NetworkHealthDashboardViewModel : ViewModelBase
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(NetworkHealthDashboardViewModel));

    private readonly DashboardAggregator _aggregator;

    private int _healthyCount;
    private int _degradedCount;
    private int _unhealthyCount;
    private int _unknownCount;
    private int _staleCount;
    private int _activeAlertCount;
    private int _monitoredDeviceCount;
    private int _interfaceUp;
    private int _interfaceDown;
    private int _interfaceAdminDown;
    private int _interfaceUnknown;

    public int HealthyCount { get => _healthyCount; private set { _healthyCount = value; OnPropertyChanged(); } }
    public int DegradedCount { get => _degradedCount; private set { _degradedCount = value; OnPropertyChanged(); } }
    public int UnhealthyCount { get => _unhealthyCount; private set { _unhealthyCount = value; OnPropertyChanged(); } }
    public int UnknownCount { get => _unknownCount; private set { _unknownCount = value; OnPropertyChanged(); } }
    public int StaleCount { get => _staleCount; private set { _staleCount = value; OnPropertyChanged(); } }
    public int ActiveAlertCount { get => _activeAlertCount; private set { _activeAlertCount = value; OnPropertyChanged(); } }
    public int MonitoredDeviceCount { get => _monitoredDeviceCount; private set { _monitoredDeviceCount = value; OnPropertyChanged(); } }
    public int InterfaceUp { get => _interfaceUp; private set { _interfaceUp = value; OnPropertyChanged(); } }
    public int InterfaceDown { get => _interfaceDown; private set { _interfaceDown = value; OnPropertyChanged(); } }
    public int InterfaceAdminDown { get => _interfaceAdminDown; private set { _interfaceAdminDown = value; OnPropertyChanged(); } }
    public int InterfaceUnknown { get => _interfaceUnknown; private set { _interfaceUnknown = value; OnPropertyChanged(); } }

    private string _monitoringStatus = "STOPPED";
    public string MonitoringStatus { get => _monitoringStatus; private set { _monitoringStatus = value; OnPropertyChanged(); } }

    private string _overallHealth = "UNKNOWN";
    public string OverallHealth { get => _overallHealth; private set { _overallHealth = value; OnPropertyChanged(); } }

    private string _lastUpdated = "—";
    public string LastUpdated { get => _lastUpdated; private set { _lastUpdated = value; OnPropertyChanged(); } }

    private string _statusMessage = string.Empty;
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }

    public ObservableCollection<DashboardDeviceRow> Devices { get; } = [];
    public ObservableCollection<DashboardAlertRow> Alerts { get; } = [];
    public ObservableCollection<DashboardEventRow> Events { get; } = [];
    public ObservableCollection<DashboardIssueRow> InterfaceIssues { get; } = [];
    public ObservableCollection<DashboardLatencyRow> Latency { get; } = [];
    public ObservableCollection<DashboardAvailabilityRow> Availability { get; } = [];

    public ObservableCollection<DashboardNotificationRow> Notifications { get; } = [];

    public ObservableCollection<string> NotificationChannels { get; } = [];

    private string _notificationStatus = string.Empty;
    public string NotificationStatus { get => _notificationStatus; private set { _notificationStatus = value; OnPropertyChanged(); } }

    public ICommand TestNotificationCommand { get; }

    public string[] StatusFilterOptions { get; } = ["All", "Healthy", "Degraded", "Unhealthy", "Stale", "Unknown"];

    private string _selectedStatusFilter = "All";
    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set { _selectedStatusFilter = value; OnPropertyChanged(); _ = RefreshAsync(); }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { _searchText = value; OnPropertyChanged(); _ = RefreshAsync(); }
    }

    private DashboardDeviceRow? _selectedDevice;
    public DashboardDeviceRow? SelectedDevice
    {
        get => _selectedDevice;
        set { _selectedDevice = value; OnPropertyChanged(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand AnalyzeWithAiCommand { get; }

    /// <summary>Raised when the user clicks "Analyze with AI" so the view can navigate to the copilot.</summary>
    public event Action? NavigateToCopilotRequested;

    public NetworkHealthDashboardViewModel(DashboardAggregator aggregator)
    {
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));

        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync());
        AnalyzeWithAiCommand = new RelayCommand(_ => AnalyzeWithAi(), _ => SelectedDevice is not null);
        TestNotificationCommand = new RelayCommand(_ => _ = TestNotificationAsync());
    }

    public Task InitializeAsync() => RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var filter = SelectedStatusFilter == "All" ? null : SelectedStatusFilter;
            var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText;

            var snapshot = await _aggregator.BuildAsync(filter, search);

            HealthyCount = snapshot.Health.Healthy;
            DegradedCount = snapshot.Health.Degraded;
            UnhealthyCount = snapshot.Health.Unhealthy;
            UnknownCount = snapshot.Health.Unknown;
            StaleCount = snapshot.Health.Stale;
            ActiveAlertCount = snapshot.Health.ActiveAlerts;
            MonitoredDeviceCount = snapshot.Health.MonitoredDevices;
            MonitoringStatus = snapshot.Health.MonitoringRunning ? "RUNNING" : "STOPPED";
            LastUpdated = snapshot.GeneratedAt.ToLocalTime().ToString("HH:mm:ss");
            InterfaceUp = snapshot.Interfaces.Up;
            InterfaceDown = snapshot.Interfaces.Down;
            InterfaceAdminDown = snapshot.Interfaces.AdminDown;
            InterfaceUnknown = snapshot.Interfaces.Unknown;

            OverallHealth = snapshot.Health.Unhealthy > 0
                ? "UNHEALTHY"
                : snapshot.Health.Degraded > 0 ? "DEGRADED" : snapshot.Health.Stale > 0 ? "STALE" : "HEALTHY";

            Devices.Clear();
            foreach (var device in snapshot.Devices)
            {
                Devices.Add(new DashboardDeviceRow
                {
                    DeviceId = device.DeviceId,
                    Name = device.Name,
                    Type = device.Type,
                    Address = device.Address,
                    Health = device.Health,
                    Snmp = device.SnmpAvailable switch { true => "✓", false => "✗", null => "—" },
                    Alerts = device.ActiveAlerts.ToString(),
                    LastCheck = device.LastCheck?.ToLocalTime().ToString("HH:mm:ss") ?? "—",
                    IsStale = device.IsStale,
                });
            }

            Alerts.Clear();
            foreach (var alert in snapshot.ActiveAlerts)
            {
                Alerts.Add(new DashboardAlertRow
                {
                    Severity = alert.Severity,
                    Status = alert.Status,
                    Title = alert.Title,
                    Target = string.IsNullOrWhiteSpace(alert.TargetName) ? alert.TargetId : alert.TargetName,
                    Occurrences = alert.Occurrences.ToString(),
                    LastSeen = alert.LastSeenAt.ToLocalTime().ToString("HH:mm:ss"),
                });
            }

            Events.Clear();
            foreach (var evt in snapshot.RecentEvents)
            {
                Events.Add(new DashboardEventRow
                {
                    Time = evt.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    Target = evt.TargetName,
                    Kind = evt.Kind.ToString(),
                    Severity = evt.Severity,
                    Summary = evt.Summary,
                });
            }

            InterfaceIssues.Clear();
            foreach (var issue in snapshot.InterfaceIssues)
            {
                InterfaceIssues.Add(new DashboardIssueRow
                {
                    Device = issue.DeviceId,
                    Interface = issue.InterfaceName,
                    Kind = issue.Kind.ToString(),
                    Summary = issue.Summary,
                });
            }

            Latency.Clear();
            foreach (var latency in snapshot.Latency)
            {
                Latency.Add(new DashboardLatencyRow
                {
                    Target = latency.TargetName,
                    Average = latency.AverageLatencyMs is { } avg ? $"{avg:0.0} ms" : "—",
                    PacketLoss = latency.PacketLossPercent is { } loss ? $"{loss:0.0}%" : "—",
                });
            }

            await LoadAvailabilityAsync();
            await LoadNotificationsAsync();

            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to refresh the network health dashboard.", ex);
            StatusMessage = "Dashboard data is currently unavailable.";
        }
    }

    private async Task LoadAvailabilityAsync()
    {
        try
        {
            var items = await _aggregator.GetAvailabilityAsync(TimeSpan.FromHours(24));
            Availability.Clear();
            foreach (var item in items)
            {
                Availability.Add(new DashboardAvailabilityRow
                {
                    Target = item.TargetName,
                    Availability = item.AvailabilityPercent is { } pct ? $"{pct:0.0}%" : "Insufficient data",
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to load dashboard availability.", ex);
        }
    }

    private void AnalyzeWithAi()
    {
        if (SelectedDevice is null)
            return;

        AiCopilotHandoff.PendingPrompt =
            $"Analyze the current health of {SelectedDevice.Name} using available monitoring evidence.";

        NavigateToCopilotRequested?.Invoke();
    }

    private async Task LoadNotificationsAsync()
    {
        try
        {
            NotificationChannels.Clear();
            foreach (var channel in NotificationComposition.Service.GetAvailableChannels())
            {
                var state = channel.IsAvailable && channel.IsConfigured
                    ? "Available"
                    : channel.IsAvailable ? "Not configured" : "Unavailable";
                NotificationChannels.Add($"{channel.Name} — {state}");
            }

            var store = PersistenceComposition.NotificationStore;
            if (store is null)
                return;

            var history = await store.GetHistoryAsync(null, null, null, null, null, 20, 0);

            Notifications.Clear();
            foreach (var notification in history)
            {
                Notifications.Add(new DashboardNotificationRow
                {
                    Time = notification.CreatedAt.ToLocalTime().ToString("HH:mm:ss"),
                    Alert = notification.AlertId,
                    Channel = notification.Channel,
                    Event = notification.EventType.ToString(),
                    Status = notification.Status.ToString(),
                    Attempts = notification.AttemptCount.ToString(),
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Failed to load notification history.", ex);
        }
    }

    private async Task TestNotificationAsync()
    {
        try
        {
            var result = await NotificationComposition.SendTestAsync();
            NotificationStatus = result is { Status: NotificationStatus.Sent }
                ? "Test notification sent."
                : $"Test notification: {result?.Status}";
        }
        catch (Exception ex)
        {
            NotificationStatus = "Test notification failed.";
            Log.Warn("Failed to send test notification.", ex);
        }
    }
}

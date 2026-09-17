using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using log4net;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;
using NETworkManager.AI.Models;
using NETworkManager.AI.Snmp;
using NETworkManager.Utilities;

namespace NETworkManager.ViewModels;

/// <summary>One row of the runtime status table.</summary>
public sealed class MonitoringRowViewModel
{
    public string TargetId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Health { get; init; } = string.Empty;
    public string LastCheck { get; init; } = "—";
    public string Observed { get; init; } = "—";
    public string LastError { get; init; } = string.Empty;
}

/// <summary>Read-only list item for a persisted profile.</summary>
public sealed class MonitoringProfileListItemViewModel : INotifyPropertyChanged
{
    private bool _enabled;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    public int TargetCount { get; init; }
    public int CheckCount { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Editable target row in the editor draft.</summary>
public sealed class MonitoringTargetItemViewModel : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _address = string.Empty;
    private string _description = string.Empty;
    private bool _enabled = true;
    private string _type = "Host";

    public string Id { get => _id; set { _id = value; OnPropertyChanged(); } }
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public string Address { get => _address; set { _address = value; OnPropertyChanged(); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
    public string Type { get => _type; set { _type = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Editable check row in the editor draft.</summary>
public sealed class MonitoringCheckItemViewModel : INotifyPropertyChanged
{
    private string _checkId = string.Empty;
    private string _targetId = string.Empty;
    private string _type = "Ping";
    private int _port = 443;
    private string _timeoutSeconds = string.Empty;
    private string _intervalSeconds = string.Empty;
    private bool _enabled = true;
    private string _snmpVersion = "V2C";
    private int _snmpPort = 161;
    private string _snmpCredentialReference = string.Empty;
    private string _snmpCollectionMode = "SystemAndInterfaces";

    public string CheckId { get => _checkId; set { _checkId = value; OnPropertyChanged(); } }
    public string TargetId { get => _targetId; set { _targetId = value; OnPropertyChanged(); } }
    public string Type { get => _type; set { _type = value; OnPropertyChanged(); } }
    public int Port { get => _port; set { _port = value; OnPropertyChanged(); } }
    public string TimeoutSeconds { get => _timeoutSeconds; set { _timeoutSeconds = value; OnPropertyChanged(); } }
    public string IntervalSeconds { get => _intervalSeconds; set { _intervalSeconds = value; OnPropertyChanged(); } }
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
    public string SnmpVersion { get => _snmpVersion; set { _snmpVersion = value; OnPropertyChanged(); } }
    public int SnmpPort { get => _snmpPort; set { _snmpPort = value; OnPropertyChanged(); } }
    public string SnmpCredentialReference { get => _snmpCredentialReference; set { _snmpCredentialReference = value; OnPropertyChanged(); } }
    public string SnmpCollectionMode { get => _snmpCollectionMode; set { _snmpCollectionMode = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Read-only display row for an active alert in the monitoring view.</summary>
public sealed class AlertRowViewModel
{
    public string AlertId { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Occurrences { get; init; } = string.Empty;
    public string FirstSeen { get; init; } = string.Empty;
    public string LastSeen { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
    public string Transition { get; init; } = string.Empty;
    public string Resolution { get; init; } = string.Empty;
    public string Classification { get; init; } = string.Empty;
}

/// <summary>Read-only display row for one persisted monitoring result.</summary>
public sealed class MonitoringHistoryRowViewModel
{
    public string Timestamp { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Check { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

/// <summary>Read-only display row for one persisted alert.</summary>
public sealed class AlertHistoryRowViewModel
{
    public string Created { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Occurrences { get; init; } = string.Empty;
    public string LastSeen { get; init; } = string.Empty;
}

/// <summary>Read-only display row for one SNMP interface telemetry sample.</summary>
public sealed class SnmpInterfaceRowViewModel
{
    public string Index { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Admin { get; init; } = string.Empty;
    public string Oper { get; init; } = string.Empty;
    public string Speed { get; init; } = string.Empty;
    public string InOctets { get; init; } = string.Empty;
    public string OutOctets { get; init; } = string.Empty;
}

/// <summary>
///     Monitoring management view model: profile CRUD + editor draft + global start/stop + runtime status.
///     All configuration goes through <see cref="IMonitoringProfileService"/>; all runtime through the shared engine.
///     No persistence logic lives here.
/// </summary>
public class NetworkMonitoringViewModel : ViewModelBase, IMonitoringObserver, IAlertObserver
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(NetworkMonitoringViewModel));

    private readonly IMonitoringProfileService _profileService;

    private MonitoringProfileDraft? _draft;

    private MonitoringProfileListItemViewModel? _selectedProfile;

    private string _editingId = string.Empty;

    private bool _isNew;

    // ----- editor fields ----------------------------------------------------

    private string _name = string.Empty;
    public string Name { get => _name; set { _name = value; OnPropertyChanged(); OnEditorChanged(); } }

    private string _description = string.Empty;
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); OnEditorChanged(); } }

    private bool _profileEnabled = true;
    public bool ProfileEnabled { get => _profileEnabled; set { _profileEnabled = value; OnPropertyChanged(); OnEditorChanged(); } }

    private string _intervalSeconds = "30";
    public string IntervalSeconds { get => _intervalSeconds; set { _intervalSeconds = value; OnPropertyChanged(); OnEditorChanged(); } }

    private string _timeoutSeconds = "5";
    public string TimeoutSeconds { get => _timeoutSeconds; set { _timeoutSeconds = value; OnPropertyChanged(); OnEditorChanged(); } }

    private string _maxConcurrency = "5";
    public string MaxConcurrency { get => _maxConcurrency; set { _maxConcurrency = value; OnPropertyChanged(); } }

    private string _editorMessage = string.Empty;
    public string EditorMessage { get => _editorMessage; private set { _editorMessage = value; OnPropertyChanged(); } }

    private string _editorTitle = "Select or add a profile";
    public string EditorTitle { get => _editorTitle; private set { _editorTitle = value; OnPropertyChanged(); } }

    private bool _isDirty;
    public bool IsDirty { get => _isDirty; private set { _isDirty = value; OnPropertyChanged(); } }

    private bool _isRunning;
    public bool IsRunning { get => _isRunning; private set { _isRunning = value; OnPropertyChanged(); } }

    private string _globalStatus = "Stopped";
    public string GlobalStatus { get => _globalStatus; private set { _globalStatus = value; OnPropertyChanged(); } }

    private int _activeTargets;
    public int ActiveTargets { get => _activeTargets; private set { _activeTargets = value; OnPropertyChanged(); } }

    private int _healthyCount;
    public int HealthyCount { get => _healthyCount; private set { _healthyCount = value; OnPropertyChanged(); } }

    private int _degradedCount;
    public int DegradedCount { get => _degradedCount; private set { _degradedCount = value; OnPropertyChanged(); } }

    private int _unhealthyCount;
    public int UnhealthyCount { get => _unhealthyCount; private set { _unhealthyCount = value; OnPropertyChanged(); } }

    // ----- collections ------------------------------------------------------

    public ObservableCollection<MonitoringProfileListItemViewModel> Profiles { get; } = [];

    public ObservableCollection<MonitoringRowViewModel> Rows { get; } = [];

    public ObservableCollection<MonitoringTargetItemViewModel> Targets { get; } = [];

    public ObservableCollection<MonitoringCheckItemViewModel> Checks { get; } = [];

    public string[] TargetTypeOptions { get; } = Enum.GetNames<MonitorTargetType>();

    public string[] CheckTypeOptions { get; } = Enum.GetNames<MonitorCheckType>();

    public ObservableCollection<AlertRowViewModel> Alerts { get; } = [];

    private AlertRowViewModel? _selectedAlert;

    public AlertRowViewModel? SelectedAlert
    {
        get => _selectedAlert;
        set
        {
            _selectedAlert = value;
            OnPropertyChanged();
        }
    }

    private string _alertCount = "0 active alerts";
    public string AlertCount { get => _alertCount; private set { _alertCount = value; OnPropertyChanged(); } }

    public ObservableCollection<MonitoringHistoryRowViewModel> HistoryRows { get; } = [];

    public ObservableCollection<AlertHistoryRowViewModel> AlertHistoryRows { get; } = [];

    public ICommand LoadHistoryCommand { get; }

    private string _historyNote = string.Empty;
    public string HistoryNote { get => _historyNote; private set { _historyNote = value; OnPropertyChanged(); } }

    // ----- SNMP credential + telemetry (Step 13) ---------------------------

    public string[] SnmpVersionOptions { get; } = Enum.GetNames<SnmpVersion>();

    public string[] SnmpCollectionModeOptions { get; } = Enum.GetNames<SnmpCollectionMode>();

    private string _snmpCredentialReference = string.Empty;
    public string SnmpCredentialReference { get => _snmpCredentialReference; set { _snmpCredentialReference = value; OnPropertyChanged(); } }

    private string _snmpCredentialVersion = "V2C";
    public string SnmpCredentialVersion { get => _snmpCredentialVersion; set { _snmpCredentialVersion = value; OnPropertyChanged(); } }

    private string _snmpCommunity = string.Empty;
    public string SnmpCommunity { get => _snmpCommunity; set { _snmpCommunity = value; OnPropertyChanged(); } }

    private string _snmpUsername = string.Empty;
    public string SnmpUsername { get => _snmpUsername; set { _snmpUsername = value; OnPropertyChanged(); } }

    private string _snmpAuthPassword = string.Empty;
    public string SnmpAuthPassword { get => _snmpAuthPassword; set { _snmpAuthPassword = value; OnPropertyChanged(); } }

    private string _snmpPrivPassword = string.Empty;
    public string SnmpPrivPassword { get => _snmpPrivPassword; set { _snmpPrivPassword = value; OnPropertyChanged(); } }

    private string _snmpCredentialStatus = string.Empty;
    public string SnmpCredentialStatus { get => _snmpCredentialStatus; private set { _snmpCredentialStatus = value; OnPropertyChanged(); } }

    private string _snmpDeviceTargetId = string.Empty;
    public string SnmpDeviceTargetId { get => _snmpDeviceTargetId; set { _snmpDeviceTargetId = value; OnPropertyChanged(); } }

    private string _snmpDeviceSummary = string.Empty;
    public string SnmpDeviceSummary { get => _snmpDeviceSummary; private set { _snmpDeviceSummary = value; OnPropertyChanged(); } }

    public ObservableCollection<SnmpInterfaceRowViewModel> SnmpInterfaces { get; } = [];

    // ----- commands ---------------------------------------------------------

    public ICommand StartMonitoringCommand { get; }
    public ICommand StopMonitoringCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand EditProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand EnableProfileCommand { get; }
    public ICommand DisableProfileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddTargetCommand { get; }
    public ICommand RemoveTargetCommand { get; }
    public ICommand AddCheckCommand { get; }
    public ICommand RemoveCheckCommand { get; }

    public ICommand AcknowledgeAlertCommand { get; }

    public ICommand ConfigureSnmpCredentialCommand { get; }
    public ICommand RemoveSnmpCredentialCommand { get; }
    public ICommand RefreshSnmpCommand { get; }

    public NetworkMonitoringViewModel(IMonitoringProfileService profileService)
    {
        _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));

        StartMonitoringCommand = new RelayCommand(_ => StartMonitoring(), _ => !IsRunning);
        StopMonitoringCommand = new RelayCommand(_ => StopMonitoring(), _ => IsRunning);
        RefreshCommand = new RelayCommand(_ => RefreshStatus());
        AddProfileCommand = new RelayCommand(_ => NewProfile(), _ => !IsDirty);
        EditProfileCommand = new RelayCommand(_ => EditProfile(), _ => SelectedProfile is not null);
        DeleteProfileCommand = new RelayCommand(_ => DeleteProfile(), _ => SelectedProfile is not null);
        EnableProfileCommand = new RelayCommand(_ => SetSelectedEnabled(true), _ => SelectedProfile is { Enabled: false });
        DisableProfileCommand = new RelayCommand(_ => SetSelectedEnabled(false), _ => SelectedProfile is { Enabled: true });
        SaveCommand = new RelayCommand(_ => _ = SaveAsync(), _ => IsDirty);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsDirty);
        AddTargetCommand = new RelayCommand(_ => AddTarget(), _ => IsEditing);
        RemoveTargetCommand = new RelayCommand(p => RemoveTarget((MonitoringTargetItemViewModel)p!), _ => IsEditing);
        AddCheckCommand = new RelayCommand(_ => AddCheck(), _ => IsEditing);
        RemoveCheckCommand = new RelayCommand(p => RemoveCheck((MonitoringCheckItemViewModel)p!), _ => IsEditing);
        AcknowledgeAlertCommand = new RelayCommand(_ => AcknowledgeSelectedAlert(), _ => SelectedAlert is { Status: "Open" });
        LoadHistoryCommand = new RelayCommand(_ => _ = LoadHistoryAsync());
        ConfigureSnmpCredentialCommand = new RelayCommand(_ => ConfigureSnmpCredential());
        RemoveSnmpCredentialCommand = new RelayCommand(_ => RemoveSnmpCredential());
        RefreshSnmpCommand = new RelayCommand(_ => _ = RefreshSnmpAsync());

        MonitoringComposition.Instance.Subscribe(this);
        AlertComposition.Alerts.Subscribe(this);
    }

    public MonitoringProfileListItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (value is not null)
                SelectProfile(value);
        }
    }

    public bool IsEditing => _draft is not null;

    public async Task InitializeAsync()
    {
        try
        {
            await _profileService.LoadAsync();
            ReloadProfiles();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load monitoring profiles.", ex);
            EditorMessage = "Unable to load monitoring profiles.";
        }

        RefreshStatus();
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            if (PersistenceComposition.History is null)
            {
                HistoryNote = PersistenceComposition.InitializationError ?? "History storage is unavailable.";
                return;
            }

            var results = await PersistenceComposition.History.GetResultsAsync(null, null, null, 50, 0);

            HistoryRows.Clear();
            foreach (var r in results)
            {
                HistoryRows.Add(new MonitoringHistoryRowViewModel
                {
                    Timestamp = r.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                    Target = r.TargetId,
                    Check = r.CheckType.ToString(),
                    Status = r.Status.ToString(),
                    Message = r.SafeMessage ?? string.Empty,
                });
            }

            var alerts = await PersistenceComposition.History.GetAlertHistoryAsync(null, null, null, 50, 0);

            AlertHistoryRows.Clear();
            foreach (var a in alerts)
            {
                AlertHistoryRows.Add(new AlertHistoryRowViewModel
                {
                    Created = a.FirstSeenAt.ToLocalTime().ToString("HH:mm:ss"),
                    Target = a.TargetName ?? a.TargetId,
                    Severity = a.Severity.ToString(),
                    Status = a.Status.ToString(),
                    Occurrences = a.OccurrenceCount.ToString(),
                    LastSeen = a.LastSeenAt.ToLocalTime().ToString("HH:mm:ss"),
                });
            }

            HistoryNote = $"{HistoryRows.Count} result(s), {AlertHistoryRows.Count} alert(s) in history.";
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load monitoring history.", ex);
            HistoryNote = "Unable to load monitoring history.";
        }
    }

    public void OnMonitoringEvent(MonitoringEvent e)
    {
        OnUi(RefreshStatus);
    }

    // ----- profile list -----------------------------------------------------

    private void ReloadProfiles()
    {
        Profiles.Clear();

        foreach (var profile in _profileService.GetProfilesAsync().GetAwaiter().GetResult())
        {
            Profiles.Add(new MonitoringProfileListItemViewModel
            {
                Id = profile.EffectiveId,
                Name = profile.Name,
                Description = profile.Description ?? string.Empty,
                Enabled = profile.Enabled,
                TargetCount = profile.Targets.Count,
                CheckCount = profile.Checks.Count,
            });
        }

        OnPropertyChanged(nameof(SelectedProfile));
    }

    private void NewProfile()
    {
        _isNew = true;
        _editingId = string.Empty;
        _draft = new MonitoringProfileDraft(new MonitoringProfile { Name = string.Empty });

        Name = string.Empty;
        Description = string.Empty;
        ProfileEnabled = true;
        IntervalSeconds = "30";
        TimeoutSeconds = "5";
        Targets.Clear();
        Checks.Clear();
        EditorTitle = "New profile";
        EditorMessage = string.Empty;
        MarkClean();
    }

    private void EditProfile()
    {
        if (_selectedProfile is null)
            return;

        var profile = _profileService.GetProfileAsync(_selectedProfile.Id).GetAwaiter().GetResult();
        if (profile is null)
            return;

        LoadIntoEditor(profile, isNew: false);
    }

    private void SelectProfile(MonitoringProfileListItemViewModel item)
    {
        if (!ConfirmDiscard())
            return;

        _selectedProfile = item;

        var profile = _profileService.GetProfileAsync(item.Id).GetAwaiter().GetResult();
        if (profile is not null)
            LoadIntoEditor(profile, isNew: false);
    }

    private void LoadIntoEditor(MonitoringProfile profile, bool isNew)
    {
        _isNew = isNew;
        _editingId = profile.EffectiveId;
        _draft = new MonitoringProfileDraft(profile);

        Name = profile.Name;
        Description = profile.Description ?? string.Empty;
        ProfileEnabled = profile.Enabled;
        IntervalSeconds = profile.DefaultInterval?.TotalSeconds.ToString("0") ?? string.Empty;
        TimeoutSeconds = profile.DefaultTimeout?.TotalSeconds.ToString("0") ?? string.Empty;

        Targets.Clear();
        foreach (var target in profile.Targets)
        {
            Targets.Add(new MonitoringTargetItemViewModel
            {
                Id = target.Id,
                Name = target.Name ?? string.Empty,
                Address = target.Address ?? string.Empty,
                Description = target.Description ?? string.Empty,
                Enabled = target.Enabled,
                Type = target.Type.ToString(),
            });
        }

        Checks.Clear();
        foreach (var check in profile.Checks)
        {
            Checks.Add(new MonitoringCheckItemViewModel
            {
                CheckId = check.CheckId,
                TargetId = check.TargetId,
                Type = check.Type.ToString(),
                Port = check.Port,
                TimeoutSeconds = check.Timeout?.TotalSeconds.ToString("0") ?? string.Empty,
                IntervalSeconds = check.Interval?.TotalSeconds.ToString("0") ?? string.Empty,
                Enabled = check.Enabled,
                SnmpVersion = check.Snmp?.Version.ToString() ?? "V2C",
                SnmpPort = check.Snmp?.Port ?? 161,
                SnmpCredentialReference = check.Snmp?.CredentialReference ?? string.Empty,
                SnmpCollectionMode = check.Snmp?.CollectionMode.ToString() ?? "SystemAndInterfaces",
            });
        }

        EditorTitle = $"Profile: {profile.Name}";
        EditorMessage = string.Empty;
        MarkClean();
    }

    private async void SetSelectedEnabled(bool enabled)
    {
        if (_selectedProfile is null)
            return;

        var ok = enabled
            ? await _profileService.EnableProfileAsync(_selectedProfile.Id)
            : await _profileService.DisableProfileAsync(_selectedProfile.Id);

        if (ok)
            ReloadProfiles();
    }

    private async void DeleteProfile()
    {
        if (_selectedProfile is null || !ConfirmDiscard())
            return;

        var id = _selectedProfile.Id;
        await _profileService.DeleteProfileAsync(id);

        _selectedProfile = null;
        ResetEditor();
        ReloadProfiles();
    }

    // ----- editor save/cancel ----------------------------------------------

    private async Task SaveAsync()
    {
        var (profile, errors) = BuildProfile();
        if (errors.Count > 0)
        {
            EditorMessage = string.Join(Environment.NewLine, errors);
            return;
        }

        try
        {
            if (_isNew)
            {
                var created = await _profileService.CreateProfileAsync(profile);
                _editingId = created.EffectiveId;
                _isNew = false;
            }
            else
            {
                await _profileService.UpdateProfileAsync(profile);
            }

            EditorMessage = "Saved.";
            ReloadProfiles();
            ResetDirtyFrom(profile);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            EditorMessage = ex.Message;
        }
    }

    private void Cancel()
    {
        _draft?.Discard();

        if (_isNew)
        {
            ResetEditor();
            return;
        }

        var profile = _profileService.GetProfileAsync(_editingId).GetAwaiter().GetResult();
        if (profile is not null)
            LoadIntoEditor(profile, isNew: false);
        else
            ResetEditor();
    }

    // ----- target/check editing --------------------------------------------

    private void AddTarget()
    {
        var n = Targets.Count + 1;
        Targets.Add(new MonitoringTargetItemViewModel
        {
            Id = $"target-{n}",
            Name = string.Empty,
            Address = string.Empty,
            Type = "Host",
            Enabled = true,
        });
        MarkDirty();
    }

    private void RemoveTarget(MonitoringTargetItemViewModel item)
    {
        Targets.Remove(item);
        MarkDirty();
    }

    private void AddCheck()
    {
        var n = Checks.Count + 1;
        Checks.Add(new MonitoringCheckItemViewModel
        {
            CheckId = $"check-{n}",
            TargetId = Targets.FirstOrDefault()?.Id ?? string.Empty,
            Type = "Ping",
            Port = 443,
            Enabled = true,
        });
        MarkDirty();
    }

    private void RemoveCheck(MonitoringCheckItemViewModel item)
    {
        Checks.Remove(item);
        MarkDirty();
    }

    // ----- start / stop -----------------------------------------------------

    private async void StartMonitoring()
    {
        if (!ConfirmDiscard())
            return;

        try
        {
            var profiles = await _profileService.GetProfilesAsync();

            var concurrency = ParseConcurrency();
            var engine = MonitoringComposition.Rebuild(concurrency);
            engine.Subscribe(this);
            engine.Subscribe(AlertComposition.Alerts);
            AlertComposition.Alerts.Start();

            MonitoringConfigurationApplier.Apply(engine, profiles);
            await engine.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start monitoring.", ex);
            EditorMessage = "Monitoring could not be started.";
        }

        RefreshStatus();
    }

    private async void StopMonitoring()
    {
        try
        {
            await MonitoringComposition.Instance.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to stop monitoring.", ex);
        }

        RefreshStatus();
    }

    private void RefreshStatus()
    {
        try
        {
            var engine = MonitoringComposition.Instance;
            IsRunning = engine.IsRunning;
            GlobalStatus = IsRunning ? "Running" : "Stopped";

            var snapshots = engine.GetCurrentStatus();

            Rows.Clear();
            foreach (var snapshot in snapshots)
            {
                Rows.Add(new MonitoringRowViewModel
                {
                    TargetId = snapshot.TargetId,
                    DisplayName = snapshot.DisplayName,
                    Health = snapshot.Health.ToString(),
                    LastCheck = snapshot.Results.Count == 0
                        ? "—"
                        : snapshot.Results.Max(r => r.Timestamp).ToLocalTime().ToString("HH:mm:ss"),
                    Observed = SummarizeObserved(snapshot),
                    LastError = LatestError(snapshot),
                });
            }

            ActiveTargets = snapshots.Count;
            HealthyCount = snapshots.Count(s => s.Health == NetworkHealthStatus.Healthy);
            DegradedCount = snapshots.Count(s => s.Health == NetworkHealthStatus.Degraded);
            UnhealthyCount = snapshots.Count(s => s.Health == NetworkHealthStatus.Unhealthy);

            RefreshAlerts();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read monitoring status.", ex);
        }
    }

    private void RefreshAlerts()
    {
        try
        {
            var active = AlertComposition.Alerts.GetActiveAlerts();

            var selectedId = SelectedAlert?.AlertId;

            Alerts.Clear();
            foreach (var alert in active)
            {
                Alerts.Add(new AlertRowViewModel
                {
                    AlertId = alert.AlertId,
                    Severity = alert.Severity.ToString(),
                    Target = alert.TargetName,
                    Status = alert.Status.ToString(),
                    Title = alert.Title,
                    Occurrences = alert.OccurrenceCount.ToString(),
                    FirstSeen = alert.FirstSeenAt.ToLocalTime().ToString("HH:mm:ss"),
                    LastSeen = alert.LastSeenAt.ToLocalTime().ToString("HH:mm:ss"),
                    Reason = alert.Reason ?? string.Empty,
                    Evidence = alert.Evidence ?? string.Empty,
                    Transition = $"{alert.PreviousHealthState} → {alert.CurrentHealthState}",
                    Resolution = alert.ResolvedAt is null ? string.Empty : $"Resolved {alert.ResolvedAt.Value.ToLocalTime():HH:mm:ss} — {alert.ResolutionEvidence}",
                    Classification = alert.FailureClassification ?? string.Empty,
                });
            }

            AlertCount = $"{Alerts.Count} active alert(s)";

            if (selectedId is not null)
                SelectedAlert = Alerts.FirstOrDefault(a => a.AlertId == selectedId);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read alert state.", ex);
        }
    }

    public void OnAlertEvent(AlertEvent e)
    {
        OnUi(RefreshAlerts);
    }

    private void AcknowledgeSelectedAlert()
    {
        if (SelectedAlert is null)
            return;

        AlertComposition.Alerts.AcknowledgeAlert(SelectedAlert.AlertId);
        RefreshAlerts();
    }

    // ----- helpers ----------------------------------------------------------

    private (MonitoringProfile Profile, List<string> Errors) BuildProfile()
    {
        var errors = new List<string>();

        var interval = ParseSeconds(IntervalSeconds);
        var timeout = ParseSeconds(TimeoutSeconds);

        if (!int.TryParse(IntervalSeconds, out _) && !string.IsNullOrWhiteSpace(IntervalSeconds))
            errors.Add("Interval must be a number of seconds.");
        if (!int.TryParse(TimeoutSeconds, out _) && !string.IsNullOrWhiteSpace(TimeoutSeconds))
            errors.Add("Timeout must be a number of seconds.");

        var profile = new MonitoringProfile
        {
            Id = _isNew ? null : _editingId,
            Name = Name,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description,
            Enabled = ProfileEnabled,
            DefaultInterval = interval,
            DefaultTimeout = timeout,
            Targets = Targets.Select(ToTarget).ToList(),
            Checks = Checks.Select(ToCheck).ToList(),
        };

        errors.AddRange(_profileService.ValidateProfile(profile));

        return (profile, errors);
    }

    private void ConfigureSnmpCredential()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SnmpCredentialReference))
            {
                SnmpCredentialStatus = "Enter a credential reference.";
                return;
            }

            var version = Enum.TryParse<SnmpVersion>(SnmpCredentialVersion, out var v) ? v : SnmpVersion.V2C;
            var hasAuth = !string.IsNullOrWhiteSpace(SnmpAuthPassword);
            var hasPriv = !string.IsNullOrWhiteSpace(SnmpPrivPassword);

            var credential = new SnmpCredential
            {
                Version = version,
                Community = string.IsNullOrWhiteSpace(SnmpCommunity) ? null : SnmpCommunity,
                Username = string.IsNullOrWhiteSpace(SnmpUsername) ? null : SnmpUsername,
                SecurityLevel = version == SnmpVersion.V3
                    ? (hasAuth && hasPriv ? SnmpSecurityLevel.AuthPriv : hasAuth ? SnmpSecurityLevel.AuthNoPriv : SnmpSecurityLevel.NoAuthNoPriv)
                    : SnmpSecurityLevel.NoAuthNoPriv,
                AuthPassword = hasAuth ? SnmpAuthPassword : null,
                PrivPassword = hasPriv ? SnmpPrivPassword : null,
            };

            var errors = credential.Validate();
            if (errors.Count > 0)
            {
                SnmpCredentialStatus = string.Join(" ", errors);
                return;
            }

            SnmpComposition.CredentialStore.StoreAsync(SnmpCredentialReference, SnmpCredentialCodec.Encode(credential))
                .GetAwaiter().GetResult();

            SnmpCommunity = string.Empty;
            SnmpAuthPassword = string.Empty;
            SnmpPrivPassword = string.Empty;

            SnmpCredentialStatus = "✓ Configured";
        }
        catch (Exception ex)
        {
            SnmpCredentialStatus = "Failed to configure credential.";
            Log.Warn("Failed to configure SNMP credential.", ex);
        }
    }

    private void RemoveSnmpCredential()
    {
        if (string.IsNullOrWhiteSpace(SnmpCredentialReference))
        {
            SnmpCredentialStatus = "Enter a credential reference.";
            return;
        }

        SnmpComposition.CredentialStore.RemoveAsync(SnmpCredentialReference).GetAwaiter().GetResult();
        SnmpCredentialStatus = "Removed.";
    }

    private async Task RefreshSnmpAsync()
    {
        if (string.IsNullOrWhiteSpace(SnmpDeviceTargetId))
        {
            SnmpDeviceSummary = "Enter a device id to view telemetry.";
            SnmpInterfaces.Clear();
            return;
        }

        var repository = PersistenceComposition.SnmpTelemetryRepository;
        if (repository is null)
        {
            SnmpDeviceSummary = "Telemetry storage is unavailable.";
            SnmpInterfaces.Clear();
            return;
        }

        try
        {
            var device = await repository.GetLatestDeviceTelemetryAsync(SnmpDeviceTargetId);
            var interfaces = await repository.GetLatestInterfaceTelemetryAsync(SnmpDeviceTargetId, 200);

            if (device is null)
            {
                SnmpDeviceSummary = "No telemetry collected for this device yet.";
                SnmpInterfaces.Clear();
                return;
            }

            SnmpDeviceSummary =
                $"sysName: {device.SysName ?? "unavailable"}   ·   {device.SysDescription ?? ""}   ·   uptime: {FormatUptime(device.Uptime)}   ·   interfaces: {device.InterfaceCount}";

            SnmpInterfaces.Clear();
            foreach (var i in interfaces)
            {
                SnmpInterfaces.Add(new SnmpInterfaceRowViewModel
                {
                    Index = i.InterfaceIndex.ToString(),
                    Name = i.Name ?? i.InterfaceIndex.ToString(),
                    Admin = i.AdminStatus.ToString(),
                    Oper = i.OperationalStatus.ToString(),
                    Speed = FormatSpeed(i.SpeedBitsPerSecond),
                    InOctets = i.InOctets?.ToString() ?? "—",
                    OutOctets = i.OutOctets?.ToString() ?? "—",
                });
            }
        }
        catch (Exception ex)
        {
            SnmpDeviceSummary = "Unable to load telemetry.";
            Log.Warn("Failed to load SNMP telemetry.", ex);
        }
    }

    private static string FormatUptime(TimeSpan? uptime) =>
        uptime is null ? "unavailable" : $"{uptime.Value.TotalDays:0.#} days";

    private static string FormatSpeed(ulong? bitsPerSecond) =>
        bitsPerSecond is null ? "—" : $"{bitsPerSecond.Value / 1_000_000_000.0:0.#} Gbps";

    private static MonitoringTarget ToTarget(MonitoringTargetItemViewModel t)
    {
        var isIp = IPAddress.TryParse(t.Address, out _);
        return new MonitoringTarget
        {
            Id = t.Id,
            Name = string.IsNullOrWhiteSpace(t.Name) ? null : t.Name,
            Hostname = isIp ? null : t.Address,
            IPAddress = isIp ? t.Address : null,
            Description = string.IsNullOrWhiteSpace(t.Description) ? null : t.Description,
            Type = Enum.TryParse<MonitorTargetType>(t.Type, out var type) ? type : MonitorTargetType.Host,
            Enabled = t.Enabled,
        };
    }

    private static MonitoringCheck ToCheck(MonitoringCheckItemViewModel c)
    {
        var type = Enum.TryParse<MonitorCheckType>(c.Type, out var t) ? t : MonitorCheckType.Ping;

        return new MonitoringCheck
        {
            CheckId = c.CheckId,
            TargetId = c.TargetId,
            Type = type,
            Port = c.Port,
            Timeout = ParseSeconds(c.TimeoutSeconds),
            Interval = ParseSeconds(c.IntervalSeconds),
            Enabled = c.Enabled,
            Snmp = type == MonitorCheckType.SnmpTelemetry
                ? new SnmpCheckConfig
                {
                    Version = Enum.TryParse<SnmpVersion>(c.SnmpVersion, out var sv) ? sv : SnmpVersion.V2C,
                    Port = c.SnmpPort,
                    CredentialReference = string.IsNullOrWhiteSpace(c.SnmpCredentialReference) ? null : c.SnmpCredentialReference,
                    CollectionMode = Enum.TryParse<SnmpCollectionMode>(c.SnmpCollectionMode, out var cm) ? cm : SnmpCollectionMode.SystemAndInterfaces,
                }
                : null,
        };
    }

    private static TimeSpan? ParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return double.TryParse(text, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    private int ParseConcurrency() =>
        int.TryParse(MaxConcurrency, out var c) && c is >= 1 and <= 64 ? c : 5;

    private void OnEditorChanged()
    {
        if (_draft is null)
            return;

        _draft.Update(new MonitoringProfile
        {
            Id = _isNew ? null : _editingId,
            Name = Name,
            Description = Description,
            Enabled = ProfileEnabled,
            DefaultInterval = ParseSeconds(IntervalSeconds),
            DefaultTimeout = ParseSeconds(TimeoutSeconds),
            Targets = Targets.Select(ToTarget).ToList(),
            Checks = Checks.Select(ToCheck).ToList(),
        });

        IsDirty = true;
    }

    private void MarkDirty() => IsDirty = true;

    private void MarkClean() => IsDirty = false;

    private void ResetDirtyFrom(MonitoringProfile profile)
    {
        _draft = new MonitoringProfileDraft(profile);
        MarkClean();
    }

    private void ResetEditor()
    {
        _draft = null;
        _selectedProfile = null;
        Name = string.Empty;
        Description = string.Empty;
        EditorTitle = "Select or add a profile";
        EditorMessage = string.Empty;
        Targets.Clear();
        Checks.Clear();
        MarkClean();
    }

    private bool ConfirmDiscard()
    {
        if (!IsDirty)
            return true;

        var result = MessageBox.Show(
            "You have unsaved changes. Save before continuing?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            SaveAsync().GetAwaiter().GetResult();
            return !IsDirty;
        }

        return result == MessageBoxResult.No;
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
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
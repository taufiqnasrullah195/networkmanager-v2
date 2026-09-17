using System;
using System.Windows;
using log4net;
using NETworkManager.ViewModels;

namespace NETworkManager.Views;

/// <summary>
///     Monitoring management view: global start/stop, profile list + editor, and live runtime status. The view binds
///     <see cref="NetworkMonitoringViewModel"/>; lifecycle is view-scoped (start/stop are explicit commands; the engine
///     is stopped gracefully when the view unloads).
/// </summary>
public partial class NetworkMonitoringView
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(NetworkMonitoringView));

    private readonly NetworkMonitoringViewModel _viewModel;

    public NetworkMonitoringView()
    {
        _viewModel = new NetworkMonitoringViewModel(MonitoringComposition.ProfileService);

        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void NetworkMonitoringView_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }

    private async void NetworkMonitoringView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await MonitoringComposition.Instance.StopAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to stop network monitoring on unload.", ex);
        }
    }
}
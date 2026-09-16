using System;
using System.Windows;
using log4net;
using NETworkManager.ViewModels;

namespace NETworkManager.Views;

/// <summary>
///     Minimal monitoring surface. Shows target/status/last-check/observed/last-error; the engine lifecycle is view
///     scoped in this step (start on load, graceful stop on unload). No business logic lives here.
/// </summary>
public partial class NetworkMonitoringView
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(NetworkMonitoringView));

    private readonly NetworkMonitoringViewModel _viewModel;

    public NetworkMonitoringView()
    {
        _viewModel = new NetworkMonitoringViewModel(MonitoringComposition.Instance.Engine);

        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void NetworkMonitoringView_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await MonitoringComposition.Instance.Engine.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to start network monitoring.", ex);
        }
    }

    private async void NetworkMonitoringView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await MonitoringComposition.Instance.Engine.StopAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to stop network monitoring.", ex);
        }
    }
}
using System;
using System.Windows;
using NETworkManager.Models;
using NETworkManager.ViewModels;

namespace NETworkManager.Views;

/// <summary>
///     Presentation-only Network Health Dashboard (Step 14). Binds <see cref="NetworkHealthDashboardViewModel"/>; the
///     view contains no monitoring/alert/SNMP/AI logic. "Analyze with AI" navigates to the existing copilot view with
///     a contextual prompt — the AI remains behind the existing tool orchestrator and policy.
/// </summary>
public partial class NetworkHealthDashboardView
{
    private readonly NetworkHealthDashboardViewModel _viewModel;

    public NetworkHealthDashboardView()
    {
        _viewModel = new NetworkHealthDashboardViewModel(DashboardComposition.Aggregator);
        _viewModel.NavigateToCopilotRequested += () => MainWindow.NavigateToApplication(ApplicationName.AICopilot);

        InitializeComponent();
        DataContext = _viewModel;
    }

    private async void NetworkHealthDashboardView_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
    }
}

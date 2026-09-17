using NETworkManager.ViewModels;

namespace NETworkManager.Views;

/// <summary>
///     Minimal AI copilot chat surface. Binds <see cref="AICopilotViewModel"/>; the view contains no business
///     logic — everything flows through the provider-neutral conversation pipeline.
/// </summary>
public partial class AICopilotView
{
    private readonly AICopilotViewModel _viewModel;

    public AICopilotView()
    {
        var session = AICopilotFactory.CreateSession();
        _viewModel = new AICopilotViewModel(session.Controller);
        _viewModel.SetProviderInfo(session.ProviderInfo);
        _viewModel.ApplyPendingPrompt();

        InitializeComponent();
        DataContext = _viewModel;

        Loaded += (_, _) => _viewModel.ApplyPendingPrompt();
    }

    private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && _viewModel.SendCommand.CanExecute(null))
        {
            _viewModel.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
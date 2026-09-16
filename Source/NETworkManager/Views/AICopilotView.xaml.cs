using NETworkManager.ViewModels;

namespace NETworkManager.Views;

/// <summary>
///     Minimal AI copilot chat surface (Step 8). Binds <see cref="AICopilotViewModel"/>; the view contains no
///     business logic — everything flows through the provider-neutral conversation service.
/// </summary>
public partial class AICopilotView
{
    private readonly AICopilotViewModel _viewModel = new(AICopilotFactory.Create());

    public AICopilotView()
    {
        InitializeComponent();
        DataContext = _viewModel;
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
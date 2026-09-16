using log4net;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Conversation;
using NETworkManager.AI.Models;
using NETworkManager.Utilities;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NETworkManager.ViewModels;

/// <summary>One tool-activity line (● running / ✓ completed / ✗ failed / − skipped) with its safe summary.</summary>
public sealed class ToolActivityDisplayItem
{
    public string Glyph { get; init; } = "●";

    public string ToolName { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public static ToolActivityDisplayItem From(ToolActivity activity) => new()
    {
        Glyph = activity.Status switch
        {
            ToolActivityStatus.Started => "●",
            ToolActivityStatus.Completed => activity.Summary == "skipped" ? "−" : "✓",
            ToolActivityStatus.Failed => "✗",
            ToolActivityStatus.Cancelled => "−",
            _ => "●",
        },
        ToolName = activity.ToolName,
        Detail = BuildDetail(activity),
    };

    private static string BuildDetail(ToolActivity activity)
    {
        var detail = string.IsNullOrWhiteSpace(activity.StepId) ? string.Empty : $"step: {activity.StepId}";

        if (!string.IsNullOrWhiteSpace(activity.Summary) && activity.Summary != "skipped")
            detail = string.IsNullOrWhiteSpace(detail) ? activity.Summary : $"{detail} — {activity.Summary}";

        if (activity.Duration is { } duration && activity.Status != ToolActivityStatus.Started)
            detail = string.IsNullOrWhiteSpace(detail) ? $"{duration.TotalMilliseconds:0} ms" : $"{detail} ({duration.TotalMilliseconds:0} ms)";

        return detail;
    }
}

/// <summary>One structured evidence line (✓/✗/⚠ + title + safe detail) projected from real evidence data.</summary>
public sealed class AIEvidenceDisplayItem
{
    public string Glyph { get; init; } = "✓";

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}

/// <summary>Display item for one structured <see cref="AIFinding"/> (observation / inference / recommendation).</summary>
public sealed class AIFindingDisplayItem
{
    public string Type { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;
}

/// <summary>
///     Chat entry: role, text, tool-activity summary, findings, and structured evidence. Property change is raised
///     for fields the view updates after the async response arrives.
/// </summary>
public sealed class AICopilotChatEntry : INotifyPropertyChanged
{
    public string Role { get; init; } = string.Empty;

    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            OnPropertyChanged();
        }
    }

    private string? _toolActivity;

    public string? ToolActivity
    {
        get => _toolActivity;
        set
        {
            _toolActivity = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<AIFindingDisplayItem> Findings { get; } = [];

    public ObservableCollection<AIEvidenceDisplayItem> Evidence { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
///     MVVM view model for the AI copilot. Delegates all work to the UI-agnostic <see cref="CopilotController"/>
///     (which wraps <see cref="IAIConversationService"/>); this class only projects results into bindable
///     collections and marshals background events onto the dispatcher. No business logic, no secrets.
/// </summary>
public class AICopilotViewModel : ViewModelBase
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AICopilotViewModel));

    private readonly CopilotController _controller;

    private string _input = string.Empty;

    public string Input
    {
        get => _input;
        set
        {
            if (value == _input)
                return;

            _input = value;
            OnPropertyChanged();
        }
    }

    private bool _isBusy;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (value == _isBusy)
                return;

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    private string _statusMessage = string.Empty;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (value == _statusMessage)
                return;

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    private string _providerInfo = "Provider: Not configured";

    public string ProviderInfo
    {
        get => _providerInfo;
        private set
        {
            if (value == _providerInfo)
                return;

            _providerInfo = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Conversation entries (user + assistant messages, findings, evidence).</summary>
    public ObservableCollection<AICopilotChatEntry> Entries { get; } = [];

    /// <summary>Live tool activity while a request runs (bound to the activity panel).</summary>
    public ObservableCollection<ToolActivityDisplayItem> ToolActivity { get; } = [];

    public ICommand SendCommand { get; }

    public ICommand CancelCommand { get; }

    public AICopilotViewModel(CopilotController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => !IsBusy && !string.IsNullOrWhiteSpace(Input));
        CancelCommand = new RelayCommand(_ => _controller.Cancel(), _ => IsBusy);

        // Background-thread events marshalled onto the UI dispatcher.
        _controller.ToolActivity += (_, activity) => OnUi(() =>
        {
            if (activity.Status == ToolActivityStatus.Started)
                StatusMessage = $"Running {activity.ToolName}...";

            ToolActivity.Add(ToolActivityDisplayItem.From(activity));
        });

        _controller.StatusMessageChanged += (_, message) => OnUi(() => StatusMessage = message);
    }

    public void SetProviderInfo(string providerInfo)
    {
        ProviderInfo = string.IsNullOrWhiteSpace(providerInfo) ? "Provider: Not configured" : providerInfo;
    }

    private async Task SendAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Input))
            return;

        var message = Input;
        Input = string.Empty;

        Entries.Add(new AICopilotChatEntry { Role = "user", Text = message });

        var assistantEntry = new AICopilotChatEntry { Role = "assistant", Text = "..." };
        Entries.Add(assistantEntry);

        IsBusy = true;
        StatusMessage = "Processing request...";
        ToolActivity.Clear();

        try
        {
            var result = await _controller.SendAsync(message).ConfigureAwait(true);

            ApplyResult(assistantEntry, result);
        }
        catch (OperationCanceledException)
        {
            assistantEntry.Text = "Request cancelled.";
        }
        catch (Exception ex)
        {
            // Detailed technical information goes to the log; the UI shows a friendly message.
            Log.Error("AI copilot request failed.", ex);
            assistantEntry.Text = "An unexpected error occurred while processing your request.";
            StatusMessage = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyResult(AICopilotChatEntry entry, AIConversationResult result)
    {
        entry.Text = result.Response.Summary ?? result.Response.RawText ?? "No response.";

        foreach (var finding in result.Response.Findings)
        {
            entry.Findings.Add(new AIFindingDisplayItem
            {
                Type = finding.Type.ToString(),
                Text = string.IsNullOrWhiteSpace(finding.Description) ? finding.Title : $"{finding.Title} — {finding.Description}",
                Evidence = string.Join(", ", finding.EvidenceReferences),
            });
        }

        // Structured evidence — projected only from AIAnalysisResponse/EvidenceContexts, never from AI text.
        foreach (var item in CopilotEvidenceProjection.Project(result.Response))
            entry.Evidence.Add(new AIEvidenceDisplayItem { Glyph = item.Glyph, Title = item.Title, Detail = item.Detail });

        if (result.ToolExecutions > 0)
            entry.ToolActivity = $"AI ran {result.ToolExecutions} tool call(s).{(result.ToolCallLimitReached ? " Tool-call limit reached." : string.Empty)}";
    }

    private static void OnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
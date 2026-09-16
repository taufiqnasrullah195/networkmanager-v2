using log4net;
using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Conversation;
using NETworkManager.Utilities;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NETworkManager.ViewModels;

/// <summary>
///     Chat entry displayed in the AI copilot view: role, text, and (for assistant messages) findings and tool activity.
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

    /// <summary>Short status shown while tools run (e.g. "AI is checking: DNS resolution").</summary>
    public string? ToolActivity
    {
        get;
        set
        {
            if (value == field)
                return;

            field = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Structured findings rendered below the assistant text (fact/inference/recommendation).</summary>
    public ObservableCollection<AIFindingDisplayItem> Findings { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Display item for one structured <see cref="NETworkManager.AI.Conversation.AIFinding"/>.</summary>
public sealed class AIFindingDisplayItem
{
    public string Type { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;
    public string Evidence { get; init; } = string.Empty;
}

/// <summary>
///     View model for the AI copilot view. Binds the provider-neutral <see cref="IAIConversationService"/> (Step 7)
///     to a minimal chat surface: message input, loading state, cancellation, tool activity, and structured findings.
/// </summary>
public class AICopilotViewModel : ViewModelBase
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(AICopilotViewModel));

    private readonly IAIConversationService _conversationService;
    private readonly string _conversationId = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _cancellationTokenSource;

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
        set
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
        set
        {
            if (value == _statusMessage)
                return;

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<AICopilotChatEntry> Entries { get; } = [];

    public ICommand SendCommand { get; }

    public ICommand CancelCommand { get; }

    public AICopilotViewModel(IAIConversationService conversationService)
    {
        _conversationService = conversationService ?? throw new ArgumentNullException(nameof(conversationService));

        SendCommand = new RelayCommand(_ => Send(), _ => !IsBusy && !string.IsNullOrWhiteSpace(Input));
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsBusy);
    }

    private async void Send()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(Input))
            return;

        var message = Input;
        Input = string.Empty;

        Entries.Add(new AICopilotChatEntry { Role = "user", Text = message });

        var assistantEntry = new AICopilotChatEntry { Role = "assistant", Text = "..." };
        Entries.Add(assistantEntry);

        IsBusy = true;
        StatusMessage = "Thinking...";

        _cancellationTokenSource = new CancellationTokenSource();

        try
        {
            var result = await _conversationService.SendAsync(message, _conversationId, _cancellationTokenSource.Token)
                .ConfigureAwait(true);

            ApplyResult(assistantEntry, result);
        }
        catch (OperationCanceledException)
        {
            assistantEntry.Text = "Cancelled.";
            StatusMessage = "Request cancelled.";
        }
        catch (Exception ex)
        {
            Log.Error("AI copilot request failed.", ex);
            assistantEntry.Text = "An unexpected error occurred while processing your request.";
            StatusMessage = "Error.";
        }
        finally
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            IsBusy = false;
            StatusMessage = string.Empty;
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

        if (result.ToolExecutions > 0)
            entry.ToolActivity = $"AI ran {result.ToolExecutions} diagnostic tool call(s).";

        if (result.ToolCallLimitReached)
            entry.ToolActivity += " Tool-call limit reached.";

        StatusMessage = result.Status.ToString();
    }

    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }
}
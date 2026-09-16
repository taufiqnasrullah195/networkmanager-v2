using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Conversation;
using NETworkManager.AI.Models;
using NETworkManager.AI.Orchestration;

namespace NETworkManager.AI.Conversation;

/// <summary>
///     UI-agnostic copilot controller: the testable core behind the WPF view model. Sends messages through
///     <see cref="IAIConversationService"/>, exposes busy state, user-friendly status messages, cancellation, and
///     forwards live <see cref="ToolActivity"/> events. Contains no WPF dependency and no business logic of its own.
/// </summary>
public sealed class CopilotController
{
    private readonly IAIConversationService _conversationService;
    private readonly ToolActivityNotifier _notifier;
    private readonly string _conversationId = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _cancellationTokenSource;
    private volatile bool _isBusy;

    public CopilotController(IAIConversationService conversationService, ToolActivityNotifier notifier)
    {
        _conversationService = conversationService ?? throw new ArgumentNullException(nameof(conversationService));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));

        _notifier.Activity += OnNotifierActivity;
    }

    /// <summary>Live tool-execution activity (may be raised on background threads).</summary>
    public event EventHandler<ToolActivity>? ToolActivity;

    /// <summary>User-friendly status text ("Processing request...", "Completed", "Request cancelled."...).</summary>
    public event EventHandler<string>? StatusMessageChanged;

    public bool IsBusy => _isBusy;

    public string ConversationId => _conversationId;

    /// <summary>Sends a user message through the conversation service. Empty input is rejected.</summary>
    public async Task<AIConversationResult> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message must not be empty.", nameof(message));

        if (_isBusy)
            throw new InvalidOperationException("A copilot request is already running.");

        _isBusy = true;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellationTokenSource = linked;

        Status("Processing request...");

        try
        {
            var result = await _conversationService.SendAsync(message, _conversationId, linked.Token).ConfigureAwait(false);

            Status(result.Status switch
            {
                ConversationStatus.Completed => "Completed",
                ConversationStatus.Cancelled => "Request cancelled.",
                ConversationStatus.Failed => "Request failed.",
                _ => result.Status.ToString(),
            });

            return result;
        }
        finally
        {
            _isBusy = false;
            _cancellationTokenSource = null;
        }
    }

    /// <summary>Cancels the active request (provider call, tool orchestration, diagnostics). Idempotent.</summary>
    public void Cancel()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // request already finished — nothing to cancel
        }
    }

    private void OnNotifierActivity(object? sender, ToolActivity activity) =>
        ToolActivity?.Invoke(this, activity);

    private void Status(string message) => StatusMessageChanged?.Invoke(this, message);
}
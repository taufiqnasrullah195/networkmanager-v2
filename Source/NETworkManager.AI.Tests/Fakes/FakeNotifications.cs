using NETworkManager.AI.Notifications;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>A collecting notification channel for tests. Can be scripted to fail a bounded number of times.</summary>
public sealed class CollectingNotificationChannel : INotificationChannel
{
    private int _calls;

    public string Name { get; init; } = "Desktop";

    public string Description { get; init; } = "Test channel";

    public bool RequiresCredential { get; init; }

    public bool IsAvailable { get; set; } = true;

    public bool IsConfigured { get; set; } = true;

    public List<NotificationRequest> Sent { get; } = [];

    /// <summary>Number of times SendAsync throws before succeeding (bounded retry scenario).</summary>
    public int FailuresBeforeSuccess { get; set; }

    public Exception? AlwaysThrow { get; set; }

    public Task SendAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        if (AlwaysThrow is not null)
            return Task.FromException(AlwaysThrow);

        if (_calls < FailuresBeforeSuccess)
        {
            _calls++;
            return Task.FromException(new InvalidOperationException("temporary channel failure"));
        }

        _calls++;
        Sent.Add(request);
        return Task.CompletedTask;
    }
}

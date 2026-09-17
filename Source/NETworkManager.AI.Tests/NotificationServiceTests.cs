using NETworkManager.AI.Notifications;
using NETworkManager.AI.Tests.Fakes;
using Xunit;

namespace NETworkManager.AI.Tests;

public class NotificationServiceTests
{
    private static NotificationRequest Request(string id = "n1") => new()
    {
        NotificationId = id,
        AlertId = "a1",
        Title = "Gateway unreachable",
        Message = "Target: gw\nSeverity: Error",
        Severity = "Error",
        Target = "gw",
        Timestamp = DateTimeOffset.UtcNow,
        EventType = NotificationEventType.AlertCreated,
        IdempotencyKey = "a1:AlertCreated:0",
    };

    private static NotificationService Build(INotificationChannel channel, NotificationRetryOptions? retry = null, INotificationStore? store = null) =>
        new(new Dictionary<string, INotificationChannel> { [channel.Name] = channel }, store ?? new NotificationStore(),
            retry, delay: _ => Task.CompletedTask);

    [Fact]
    public async Task Sends_to_channel_and_records_sent()
    {
        var channel = new CollectingNotificationChannel();
        var store = new NotificationStore();
        var service = Build(channel, store: store);

        var results = await service.SendAsync(Request(), new[] { channel.Name });

        var result = Assert.Single(results);
        Assert.Equal(NotificationStatus.Sent, result.Status);
        Assert.Equal(1, result.AttemptCount);
        Assert.Single(channel.Sent);

        var history = await store.GetHistoryAsync(null, null, null, null, null, 10, 0);
        Assert.Single(history);
        Assert.Equal(NotificationStatus.Sent, history[0].Status);
    }

    [Fact]
    public async Task Temporary_failure_retries_then_succeeds()
    {
        var channel = new CollectingNotificationChannel { FailuresBeforeSuccess = 2 };
        var service = Build(channel, new NotificationRetryOptions { MaxAttempts = 3 });

        var results = await service.SendAsync(Request(), new[] { channel.Name });

        var result = Assert.Single(results);
        Assert.Equal(NotificationStatus.Sent, result.Status);
        Assert.Equal(3, result.AttemptCount); // 2 failures + 1 success
    }

    [Fact]
    public async Task Retry_limit_is_respected_and_failed_stays_failed()
    {
        var channel = new CollectingNotificationChannel { AlwaysThrow = new InvalidOperationException("boom") };
        var service = Build(channel, new NotificationRetryOptions { MaxAttempts = 3 });

        var results = await service.SendAsync(Request(), new[] { channel.Name });

        var result = Assert.Single(results);
        Assert.Equal(NotificationStatus.Failed, result.Status);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal("InvalidOperationException", result.FailureReason);
        Assert.Empty(channel.Sent);
    }

    [Fact]
    public async Task Unavailable_channel_returns_failed_without_sending()
    {
        var channel = new CollectingNotificationChannel { IsAvailable = false };
        var service = Build(channel);

        var result = Assert.Single(await service.SendAsync(Request(), new[] { channel.Name }));

        Assert.Equal(NotificationStatus.Failed, result.Status);
        Assert.Empty(channel.Sent);
    }

    [Fact]
    public async Task Unconfigured_channel_returns_failed()
    {
        var channel = new CollectingNotificationChannel { IsConfigured = false };
        var service = Build(channel);

        var result = Assert.Single(await service.SendAsync(Request(), new[] { channel.Name }));

        Assert.Equal(NotificationStatus.Failed, result.Status);
        Assert.Contains("not configured", result.FailureReason);
    }

    [Fact]
    public async Task Unknown_channel_is_skipped()
    {
        var service = Build(new CollectingNotificationChannel());
        var results = await service.SendAsync(Request(), new[] { "Nonexistent" });
        Assert.Empty(results);
    }

    [Fact]
    public async Task Cancellation_returns_cancelled()
    {
        var channel = new CollectingNotificationChannel { AlwaysThrow = new OperationCanceledException() };
        var service = Build(channel);

        var result = Assert.Single(await service.SendAsync(Request(), new[] { channel.Name }));

        Assert.Equal(NotificationStatus.Cancelled, result.Status);
    }

    [Fact]
    public void Available_channels_report_metadata()
    {
        var service = Build(new CollectingNotificationChannel { Name = "Desktop", RequiresCredential = false });

        var metadata = Assert.Single(service.GetAvailableChannels());
        Assert.Equal("Desktop", metadata.Name);
        Assert.True(metadata.IsAvailable);
        Assert.False(metadata.RequiresCredential);
    }
}

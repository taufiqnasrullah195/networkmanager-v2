using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Notifications;

namespace NETworkManager;

/// <summary>Local desktop notification channel. Records + logs the delivery (a native toast popup is a follow-up).</summary>
public sealed class DesktopNotificationChannel : INotificationChannel
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(DesktopNotificationChannel));

    public string Name => "Desktop";

    public string Description => "Local desktop notification (logged; native toast is a follow-up refinement).";

    public bool RequiresCredential => false;

    public bool IsAvailable => true;

    public bool IsConfigured => true;

    public Task SendAsync(NotificationRequest request, CancellationToken cancellationToken = default)
    {
        Log.Info($"NOTIFICATION [{request.EventType}] {request.Title} — {request.Target} ({request.Severity}).");
        return Task.CompletedTask;
    }
}

/// <summary>log4net-backed notification observability (ids/status only — no secrets).</summary>
public sealed class Log4netNotificationLogger : INotificationLogger
{
    private static readonly ILog Log = LogManager.GetLogger(typeof(NotificationEngine));

    public void Queued(string notificationId, string alertId) => Log.Debug($"Notification queued: '{notificationId}' (alert '{alertId}').");
    public void Sent(string notificationId, string channel) => Log.Info($"Notification '{notificationId}' sent via {channel}.");
    public void Failed(string notificationId, string channel, string reason) => Log.Warn($"Notification '{notificationId}' failed via {channel} ({reason}).");
    public void Retry(string notificationId, string channel, int attempt) => Log.Debug($"Notification '{notificationId}' retry {attempt} via {channel}.");
    public void SuppressedByCooldown(string alertId) => Log.Debug($"Notification suppressed by cooldown for alert '{alertId}'.");
    public void Escalated(string alertId, int level) => Log.Info($"Alert '{alertId}' escalated to level {level}.");
    public void RecoverySent(string alertId) => Log.Info($"Recovery notification sent for alert '{alertId}'.");
    public void Grouped(int count) => Log.Info($"{count} alerts grouped into one notification.");
}

/// <summary>
///     Composition root for the notification subsystem (Step 15). Builds the desktop channel, the notification service
///     (with bounded retry + persistence), and the notification engine (policy + cooldown + escalation), and subscribes
///     the engine to the alert engine's lifecycle events. Notification failures never touch alert state.
/// </summary>
public static class NotificationComposition
{
    private static readonly object Gate = new();

    private static NotificationService? _service;
    private static NotificationEngine? _engine;

    public static NotificationService Service
    {
        get
        {
            lock (Gate)
                return _service ??= BuildService();
        }
    }

    public static NotificationEngine Engine
    {
        get
        {
            lock (Gate)
                return _engine ??= BuildEngine();
        }
    }

    /// <summary>Sends a clearly-identified test notification (does not create or modify any alert).</summary>
    public static async Task<Notification?> SendTestAsync()
    {
        var request = new NotificationRequest
        {
            NotificationId = Guid.NewGuid().ToString("N"),
            AlertId = "test",
            Title = "TheWiseNetwork test notification",
            Message = "This is a test notification.",
            Severity = "Info",
            Target = "test",
            Timestamp = DateTimeOffset.UtcNow,
            EventType = NotificationEventType.AlertCreated,
            IdempotencyKey = $"test:{Guid.NewGuid():N}",
        };

        var results = await Service.SendAsync(request, new[] { "Desktop" });
        return results.FirstOrDefault();
    }

    private static NotificationService BuildService()
    {
        var channels = new Dictionary<string, INotificationChannel>
        {
            ["Desktop"] = new DesktopNotificationChannel(),
        };

        return new NotificationService(channels, PersistenceComposition.NotificationStore ?? new NotificationStore());
    }

    private static NotificationEngine BuildEngine()
    {
        var engine = new NotificationEngine(
            Service,
            new NotificationPolicy(new NotificationPolicyConfig()),
            PersistenceComposition.NotificationStore ?? new NotificationStore(),
            AlertComposition.Alerts,
            new NotificationEngineOptions(),
            new Log4netNotificationLogger());

        AlertComposition.Alerts.Subscribe(engine);
        return engine;
    }
}

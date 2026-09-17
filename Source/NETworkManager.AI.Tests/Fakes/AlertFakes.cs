using NETworkManager.AI.Abstractions;
using NETworkManager.AI.Alerts;
using NETworkManager.AI.Monitoring;

namespace NETworkManager.AI.Tests.Fakes;

/// <summary>Captures alert lifecycle events for assertions.</summary>
public sealed class CapturingAlertObserver : IAlertObserver
{
    private readonly object _gate = new();
    private readonly List<AlertEvent> _events = new();

    public void OnAlertEvent(AlertEvent e)
    {
        lock (_gate)
            _events.Add(e);
    }

    public IReadOnlyList<AlertEvent> Events
    {
        get { lock (_gate) return _events.ToList(); }
    }

    public IReadOnlyList<AlertEvent> Created => Events.Where(e => e.Type == AlertEventType.AlertCreated).ToList();
    public IReadOnlyList<AlertEvent> Resolved => Events.Where(e => e.Type == AlertEventType.AlertResolved).ToList();
    public IReadOnlyList<AlertEvent> Acknowledged => Events.Where(e => e.Type == AlertEventType.AlertAcknowledged).ToList();
    public IReadOnlyList<AlertEvent> Updated => Events.Where(e => e.Type == AlertEventType.AlertUpdated).ToList();
}

/// <summary>Captures alert logger lines for secret-safety assertions.</summary>
public sealed class CapturingAlertLogger : IAlertLogger
{
    private readonly object _gate = new();
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines
    {
        get { lock (_gate) return _lines.ToList(); }
    }

    public void AlertCreated(string alertId, AlertSeverity severity) => Add($"Created:{alertId}:{severity}");
    public void AlertUpdated(string alertId) => Add($"Updated:{alertId}");
    public void AlertAcknowledged(string alertId) => Add($"Acknowledged:{alertId}");
    public void AlertResolved(string alertId) => Add($"Resolved:{alertId}");
    public void AlertRuleError(string message) => Add($"Error:{message}");

    private void Add(string line)
    {
        lock (_gate)
            _lines.Add(line);
    }
}

/// <summary>Builds monitoring results and health-state changes for alert tests.</summary>
public static class AlertTestData
{
    public static MonitoringResult PingResult(string targetId, MonitoringResultStatus status, int received = 0, int sent = 4, int timedOut = 0) => new()
    {
        CheckId = $"{targetId}-ping",
        TargetId = targetId,
        CheckType = MonitorCheckType.Ping,
        Status = status,
        ErrorClassification = status == MonitoringResultStatus.Unhealthy ? MonitorErrorClass.Unreachable : MonitorErrorClass.None,
        Timestamp = DateTimeOffset.UtcNow,
        Duration = TimeSpan.Zero,
        SafeMessage = status == MonitoringResultStatus.Healthy ? "ICMP echo reply received." : "ICMP echo request got no reply; host availability could not be confirmed via ICMP.",
        Observed = new NETworkManager.AI.Models.PingResult
        {
            Target = targetId,
            Success = status == MonitoringResultStatus.Healthy,
            Sent = sent,
            Received = received,
            TimedOutCount = timedOut,
        },
        CorrelationId = $"{targetId}-ping",
    };

    public static HealthStateChange Change(string targetId, NetworkHealthStatus previous, NetworkHealthStatus next, MonitoringResult? result = null, string? displayName = null) => new()
    {
        TargetId = targetId,
        DisplayName = displayName ?? targetId,
        Previous = previous,
        New = next,
        Timestamp = DateTimeOffset.UtcNow,
        Reason = result?.SafeMessage ?? "reason",
        RelatedResult = result,
    };
}
using NETworkManager.AI.Abstractions;

namespace NETworkManager.AI.Alerts;

/// <summary>
///     Thread-safe in-memory alert store. Keeps every alert (resolved ones too) for history, exposes active +
///     recent queries, and bounds the retained set. A durable store can replace it behind <see cref="IAlertStore"/>.
/// </summary>
public sealed class AlertStore : IAlertStore
{
    private const int RetainedLimit = 2000;

    private readonly object _gate = new();
    private readonly Dictionary<string, Alert> _alerts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Alert> _ordered = new();

    public Alert CreateAlert(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        lock (_gate)
        {
            _alerts[alert.AlertId] = alert;
            _ordered.Add(alert);
            TrimLocked();
            return alert;
        }
    }

    public bool UpdateAlert(Alert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        lock (_gate)
        {
            if (!_alerts.ContainsKey(alert.AlertId))
                return false;

            _alerts[alert.AlertId] = alert;

            var index = _ordered.FindIndex(a => string.Equals(a.AlertId, alert.AlertId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _ordered[index] = alert;

            return true;
        }
    }

    public Alert? GetAlert(string alertId)
    {
        lock (_gate)
            return _alerts.TryGetValue(alertId, out var alert) ? alert : null;
    }

    public IReadOnlyList<Alert> GetActiveAlerts()
    {
        lock (_gate)
            return _alerts.Values
                .Where(a => a.Status is AlertStatus.Open or AlertStatus.Acknowledged)
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.FirstSeenAt)
                .ToList();
    }

    public IReadOnlyList<Alert> GetRecentAlerts(int maxCount)
    {
        lock (_gate)
        {
            var count = Math.Min(Math.Max(maxCount, 0), _ordered.Count);
            return Enumerable.Range(0, count)
                .Select(i => _ordered[_ordered.Count - 1 - i])
                .ToList();
        }
    }

    private void TrimLocked()
    {
        while (_ordered.Count > RetainedLimit)
        {
            var removed = _ordered[0];
            _ordered.RemoveAt(0);
            _alerts.Remove(removed.AlertId);
        }
    }
}
using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Abstractions;

/// <summary>Alert persistence boundary (in-memory in this step; a durable store can replace it later).</summary>
public interface IAlertStore
{
    /// <summary>Creates an alert (assigns its identity) and returns the stored value.</summary>
    Alert CreateAlert(Alert alert);

    /// <summary>Replaces an existing alert with an updated value (id unchanged).</summary>
    bool UpdateAlert(Alert alert);

    Alert? GetAlert(string alertId);

    /// <summary>Open + acknowledged alerts.</summary>
    IReadOnlyList<Alert> GetActiveAlerts();

    /// <summary>Most recent alerts (any status), most recent first, up to <paramref name="maxCount"/>.</summary>
    IReadOnlyList<Alert> GetRecentAlerts(int maxCount);
}
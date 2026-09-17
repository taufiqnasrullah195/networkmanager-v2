using NETworkManager.AI.Alerts;

namespace NETworkManager.AI.Abstractions;

/// <summary>Read-only alert surface exposed to the AI tool. Cannot acknowledge/resolve/mutate.</summary>
public interface IAlertQuery
{
    IReadOnlyList<Alert> GetActiveAlerts();

    Alert? GetAlert(string alertId);

    IReadOnlyList<Alert> GetRecentAlerts(int maxCount);
}